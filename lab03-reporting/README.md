# Lab 03: Where reporting logic runs

## Formål

Sammenligne de fire metoder til at beregne daily captured revenue, og bestem hvilken en, der skal bruges:
* sql query
* function
* view
* trigger table

## Konklusion

Valget blev en function: `captured_revenue_for_day(operator_id, date)`.
Her er authority `payments`, som funktionen læser direkte fra.
Hvis read cost senere bliver et problem, er planen at bruge et view i stedet
Refresh skal være et planlagt job, og ikke som trigger på payments.

Funktionen `captured_revenue_for_day(operator_id, date)`. `payments` er authority, og funktionen læser direkte derfra, så der ikke ligger en kopi, der kan blive stale (MV) eller forkert (trigger). Hvis read cost bliver et problem, skiftes der til en MV med scheduled refresh, men refresh må ikke ligge i en trigger på `payments`. [Begrundelse](#begrundelse).

## Run

```powershell
docker compose up -d
docker compose exec -T postgres psql -U mobility -d mobility -f /lab/base_revenue.sql
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/migrations/020_reporting_function.sql
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/migrations/021_daily_revenue_trigger.sql
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/migrations/022_daily_captured_revenue.sql
docker compose exec -T postgres psql -U mobility -d mobility -f /lab/reporting_cases.sql
docker compose down -v
```

`db/` er slut tilstanden fra lab 02 (`012_ticketing_integrity.sql` er lab 02's `011`-migration). `migrations/` køres manuelt, for at se baseline før de fire mekanismer.

## Evidens

Alle tal er for revenue_date: 2026-04-29. 

`OP-BUS price/count | OP-METRO price/count`

| State                     | Direct query                                                                                                                  | Function            | Materialized view                                         | Trigger table |
|---------------------------|-------------------------------------------------------------------------------------------------------------------------------|---------------------|-----------------------------------------------------------|---------------|
| Baseline                  | 36.00/1 \| 36.00/1                                                                                                            | 36.00/1 \| 36.00/1  | ERROR 55000                                               | 0 rækker      |
| Baseline efter MV refresh | 36.00/1 \| 36.00/1                                                                                                            | 36.00/1 \| 36.00/1  | 36.00/1 \| 36.00/1                                        | 0 rækker      |
| 1 captured insert         | 36.00/1 \| 72.00/2                                                                                                            | 36.00/1 \| 72.00/2  | 36.00/1 \| 36.00/1<br/>efter refresh: 36.00/1 \| 72.00/2  | - \| 36.00/1  |
| 2 failed insert           | 36.00/1 \| 72.00/2                                                                                                            | 36.00/1 \| 72.00/2  | 36.00/1 \| 36.00/1<br/>efter refresh: 36.00/1 \| 72.00/2  | - \| 36.00/1  |
| 3 Failed > Captured       | 36.00/1 \| 108.00/3                                                                                                           | 36.00/1 \| 108.00/3 | 36.00/1 \| 72.00/2<br/>efter refresh: 36.00/1 \| 108.00/3 | - \| 36.00/1  |
| 4 Captured > Refunded     | 36.00/1 \| 72.00/2                                                                                                            | 36.00/1 \| 72.00/2  | 36.00/1 \| 108.00/3<br/>efter refresh: 36.00/1 \| 72.00/2 | - \| 36.00/1  |
| 5 delete                  | 36.00/1 \| 36.00/1                                                                                                            | 36.00/1 \| 36.00/1  | 36.00/1 \| 72.00/2<br/>efter refresh: 36.00/1 \| 36.00/1  | - \| 36.00/1  |
| 6 duplikat external ref   | unique constraint<br/>`23505: ERROR: pq: duplicate key value violates unique constraint "payments_external_reference_unique"` | -                   | -                                                         | -             |

### Noter

- MV oprettes uden data. Første select fejler i stedet for at give nul rækker:
  `ERROR: materialized view "daily_captured_revenue" has not been populated`, SQLSTATE 55000.
  Rapporten er utilgængelig og vil få appen til at crashe.
- Funktionen tager én operator og én dato, så caller skal kende id på alle operators og kalde N gange.
  Direct query giver alle operatører i ét resultat.

- Trigger kører ved hvert insert selvom status ikke er Captured.

- UPDATE fra 'Failed' til 'Captured' opdaterede ikke trigger tabellen, og MV var stadig stale før update.
- Kan være trigger skal være på alle ændringer, og i stedet trigger en refresh på MV?

### Uenighed mellem to mekanismer

MV og trigger er ikke enige, fordi triggeren kun kører på inserts, ikke på seed data.
Da payments er authority, er det MV der er korrekt, og trigger-tabellen der er forkert.

### Side-effect trace

#### Captured insert

1. Check constraint: Not null, FK, Checks, Unique
2. Trigger executor, opdaterer derived view, hvis den fejler
   vil transaktionen blive rullet tilbage og fejle insert.
3. `payments` og `daily_revenue_by_operator` får write lock,
   og resten read lock fra joinet.
4. Returnerer 0 / 1 med antal affected rows.
5. Direct query + function + trigger table vil afspejle det med det samme.
6. Materialized view vil være stale indtil det er refreshed.

### Responsibility matrix

| Criteria               | Direct query             | Function                 | Materialized view                        | Trigger table                                                                                           |
|------------------------|--------------------------|--------------------------|------------------------------------------|---------------------------------------------------------------------------------------------------------|
| Correctness            | Always correct           | Always correct           | Stale but correct after refresh          | Real-time on new data<br/> need backfill<br/> ikke korrekt hvis en status går fra captured til refunded |
| Freshness              | Real-time                | Real-time                | Stale on new inserts                     | Fresh on new inserts                                                                                    |
| Write cost             | None                     | None                     | None initially, only on refresh          | Extra write to table                                                                                    |
| Read cost              | Need to read join tables | Need to read join tables | Low: already indexed                     | Low: already indexed                                                                                    |
| Hidden side effects    | None                     | None                     | Blocks other readers from MV             | If payments are refunded data is incorrect, can abort transaction<br/>if write to derived table fails   |
| Rebuildability         | Nothing to rebuild       | Nothing to rebuild       | Needs refresh                            | If deleted needs to fill in the data manually                                                           |
| Operational complexity | Needs to know the query  | Harder to debug          | Can become slow with size, hard to debug | Needs to be maintained if something changes, like an extra status, hard to debug                        |
| Authority              | Payments                 | Payments                 | Payments                                 | Payments                                                                                                |

### Begrundelse

Payments er authority. Vi har egentlig kun brug for query. Funktionen er en wrapped query. MV og trigger er derived
kopier.
Valget er funktionen, fordi det er samme SQL for alle callers/apps, i stedet for at bygge
deres egen query.
Derved har vi ikke en kopi liggende som kan være stale (MV) eller ikke korrekt (trigger), og logikken er ét sted.
Funktionen tager operator_id og date, som passer med access patterns som siger rapport pr. operatør.

Grunden til vi ikke vælger et view er fordi read cost ikke er et problem på nuværende tidspunkt.
Trigger tabellen kan i nuværende form, ikke garantere at data er korrekt og MV kan blive stale.

Hvis read cost bliver et problem er planen at:

- Skifte til MV - access pattern tillader eventually consistent på reporting.
- Authority forbliver payments. Freshness: scheduled refresh + timestamp for sidste refresh.
  Rebuild: refresh fra payments.
- Trigger skal også reagere på updates hvis den skal bruges.
- Refresh må ikke ligge i en trigger på payments, for så bliver en insert bundet op på at det lykkedes.

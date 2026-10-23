# Lab 04: Product identity migration

## Formål

Udfase product_code fra tickets og bruge product_id i stedet.
Samtidig skal dataintegriteten bibeholdes i gennem migrationen.
* add product_id without removing product_code.
* copy the existing ticket references to the new column.
* make sure both od and new data can still be read.
* verify that the migration worked.
* finally remove the old way of referencing products.

## Konklusion
030: 
- Tilføjer id til products, med en random uuid og NOT NULL.
- Tilføjer product_id til tickets, og tilføjer en FOREIGN key constraint
med not valid, hvilket betyder, at det kun gælder for nye rækker.

031:
- Backfiller product_id i tickets, med det nye uuid i products, ud fra til tilsvarende product_code.

032: 
- Gør product_id til et krav på tickets, hvilket betyder, at den gamle writer
nu ikke virker mere.

033:
- Dropper product_code fra tickets

Priser og valuta er uændrede fra 030-033, som det kan ses i punkt 11.

Det er vigtigt at triggeren add_product_code bliver droppet i samme transaktion som 033, ellers er der et vindue, hvor den nye writer kan fejle - se punkt 10.

Plan for migration:

Punkterne under et step i rækkefølgen:
* Hvad der skal gøres.
* Gate før næste step kan køres.
* Rollback.

1. 
* 030 + product_code_trigger i samme deploy.
* Gamle writer og reader virker stadig
* Drop trigger, product_id og products.id.

2. 
* Deploy new_reader overalt.
* Skal kunne læse både gamle og nye rækker.
* Deploy old_reader igen. Det virker, fordi product_code stadig bliver udfyldt.

3. 
* Deploy new_writer.
* Nye rækker får både id og code.
* Skift tilbage til old_writer.

4. 
* Backfill med 031.
* verify.sql skal give 0 rækker.
* Backfill er idempotent, så vi behøver ikke at rulle tilbage.

5. 
* Stop alle old_writer, kør 031 igen og derefter verify.
* verify giver 0 rækker, ellers får vi problemet fra punkt 8 hvor en ticket ikke har fået sat product_id.
* Start old_writer igen.

6. 
* Kør 032 så product_id er NOT NULL og FK valideres.
* Holde øje med at der ikke kommer fejl, og tjekke at der ikke kører nogle
old_readers.

7. 
* Kør 033 og fjern product_code_trigger samtidig.
* Point of no return for gamle clients.
* Ingen rollback. Kan tilføje product_code igen og backfill products.code.
Det er ikke et problem, fordi products.code stadig findes.

## Run

```powershell
docker compose up -d
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/scripts/baseline.sql
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/migrations/030_expand_product_identity.sql
# resten i rækkefølgen fra afsnittene herunder
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/tests/test_suite.sql
docker compose down -v
```

`db/` er slutstanden fra lab 03 (lab 02's integrity og lab 03's 020–022). `migrations/` (030–033) og `scripts/` køres manuelt, ét trin ad gangen. Test suiten kræver, at 030–033 er kørt.

## Evidens

### Noter

- `product_code_trigger.sql` køres efter 030. Ellers fejler new_writer på `product_code NOT NULL`.

### Setup

- Starter fra tom db: `docker compose down -v` og `docker compose up -d`
- 011, 020, 021 og 022 ligger i `db/` og køres automatisk
- Scripts køres med:
  `docker compose exec -T postgres psql -U mobility -d mobility -f /lab/scripts/<fil>.sql`

### 1. Baseline

```
 current_database
------------------
 mobility
(1 row)

    id    | product_code | price | currency
----------+--------------+-------+----------
 TICKET-1 | SINGLE       | 36.00 | DKK
 TICKET-2 | SINGLE       | 36.00 | DKK
 TICKET-3 | DAY          | 80.00 | DKK
(3 rows)

 id | product_code
----+--------------
(0 rows)

DO
```

- `(0 rows)` betyder at alle tickets har et gyldigt produkt.
- `DO` tjekker at der er minimum 3 tickets med 2 produkter, og ingen uden produkt.

### 2. 030 expand

```
    id    | product_code | product_id
----------+--------------+------------
 TICKET-1 | SINGLE       |
 TICKET-2 | SINGLE       |
 TICKET-3 | DAY          |
(3 rows)
```

- Tilføjer `product_id`, har ikke rørt `product_code` endnu.
- `product_id` er null og skal backfilles.

### 3. product_code_trigger

Sætter `product_code` ved nye inserts når `product_id` er sat.
Uden trigger ville new_writer fejle, fordi den ikke indsætter `product_code`,
hvilket ville throw fordi den stadig er NOT NULL.

Den kan ikke køres før 030, fordi `product_id` ikke findes endnu.

### 4. old_writer_step4.sql (TICKET-6)

output fra: ``select id, product_code, product_id from tickets order by id;``

- id - product_code - product_id
- TICKET-1 - SINGLE - NULL
- TICKET-2 - SINGLE - NULL
- TICKET-3 - DAY - NULL
- TICKET-6 - SINGLE - NULL

`product_id` er stadig null, fordi betingelsen for at triggeren kører, er at `product_id`
bliver sat ind, og derved sætter `product_code` ud fra id.

### 5. old_reader

```
    id    | user_id |        trip_id        | ticket_code  |  status   | product_code |     valid_from_utc     |      valid_to_utc      | price | currency | product_id
----------+---------+-----------------------+--------------+-----------+--------------+------------------------+------------------------+-------+----------+------------
 TICKET-1 | USER-1  | TRIP-M2-20260429-0800 | CODE-M2-0001 | Active    | SINGLE       | 2026-04-29 07:45:00+00 | 2026-04-29 10:00:00+00 | 36.00 | DKK      |
 TICKET-2 | USER-2  | TRIP-5C-20260429-0900 | CODE-5C-0001 | Validated | SINGLE       | 2026-04-29 08:45:00+00 | 2026-04-29 11:00:00+00 | 36.00 | DKK      |
 TICKET-6 | USER-1  | TRIP-M2-20260429-0800 | CODE-M2-0003 | Active    | SINGLE       | 2026-04-29 07:45:00+00 | 2026-04-29 10:00:00+00 | 36.00 | DKK      |
(3 rows)
```

- old_reader virker stadig efter 030, men `product_id` er tom ligesom de gamle tickets.

### 6. new_writer (TICKET-7) og new_reader

output fra: ``select id, product_code, product_id from tickets order by id;``

- id - product_code - product_id
- TICKET-1 - SINGLE - NULL
- TICKET-2 - SINGLE - NULL
- TICKET-3 - DAY - NULL
- TICKET-6 - SINGLE - NULL
- TICKET-7 - SINGLE - d361ee71-126e-479b-8760-ecff30faf420

Trigger virker nu, fordi vi indsætter `product_id`
og `product_code` indsættes automatisk.

output fra new_reader:

```
    id    |         resolved_product_id          | price | currency 

----------+--------------------------------------+-------+----------
TICKET-1 | d361ee71-126e-479b-8760-ecff30faf420 | 36.00 | DKK
TICKET-2 | d361ee71-126e-479b-8760-ecff30faf420 | 36.00 | DKK
TICKET-3 | 2b9ff9b6-0f7a-413f-ad74-7a0db3231613 | 80.00 | DKK
TICKET-6 | d361ee71-126e-479b-8760-ecff30faf420 | 36.00 | DKK
TICKET-7 | d361ee71-126e-479b-8760-ecff30faf420 | 36.00 | DKK
(5 rows)
```

Finder `product_id` ud fra `product_code`, hvis der ikke er et id.
Det betyder at vi kan bruge new_reader efter 030 er kørt, så `product_id` kolonnen eksisterer.
Men new_reader virker også før data er backfilled, fordi den har to conditions,
hvis id ikke er null, bruges det, ellers joiner vi på code.

### 7. 031 backfill

output fra: ``select id, product_code, product_id from tickets order by id;``

- id - product_code - product_id
- TICKET-1 - SINGLE - d361ee71-126e-479b-8760-ecff30faf420
- TICKET-2 - SINGLE - d361ee71-126e-479b-8760-ecff30faf420
- TICKET-3 - DAY - 2b9ff9b6-0f7a-413f-ad74-7a0db3231613
- TICKET-6 - SINGLE - d361ee71-126e-479b-8760-ecff30faf420
- TICKET-7 - SINGLE - d361ee71-126e-479b-8760-ecff30faf420

Efter backfill finder new_reader `product_id` ud fra p_new condition
i stedet for p_old, fordi `product_id` er sat.

### 8. Send ticket fra old_writer_step8.sql, verify, backfill igen, verify

Kør old_writer_step8.sql:
output fra `select id, product_code, product_id from tickets order by id;`

- TICKET-8 - SINGLE - NULL

Den nye række får ikke sat product_id.

Kør verify.sql:
Finder alle rows hvor
`tickets.product_id` er NULL,
`product.id` som en ticket refererer er NULL = produktet findes ikke.
`tickets.product_code` ikke matcher `products.code`

```
    id    | product_code | product_id 
----------+--------------+------------
 TICKET-8 | SINGLE       | 
(1 row)
```

Output er ticket fra old_writer_step8.sql, fordi product_id er NULL

Kør backfill og verify igen:
Output fra 031_backfill:
`UPDATE 1`

Rammer kun TICKET-8 (1 row) fordi de andre har `product_id`:
`where t.product_id is null`

Kør backfill igen:
Output fra 031_backfill:
`UPDATE 0`

Output fra verify.sql:

```
 id | product_code | product_id 
----+--------------+------------
(0 rows)
```

Output fra `select id, product_code, product_id from tickets where id = 'TICKET-8'`:

```
- TICKET-8 - SINGLE - d361ee71-126e-479b-8760-ecff30faf420
```

`product_id` på TICKET-8 er opdateret.

### 9. 032 og old_writer (skal afvises)

Output fra 032:

```
BEGIN
SET
ALTER TABLE
ALTER TABLE
COMMIT
```

Her bliver `t.product_id` altered til not null, hvilket vil få
old_writer til at fejle.

Output old_writer_step9.sql:

```
ERROR:  null value in column "product_id" of relation "tickets" violates not-null constraint
DETAIL:  Failing row contains (TICKET-9, USER-1, TRIP-M2-20260429-0800, CODE-M2-0006, Active, SINGLE, 2026-04-29 07:45:00+00, 2026-04-29 10:00:00+00, 36.00, DKK, null).
```

Trigger virker kun fra id til code.

### 10. 033 drop og triggeren

Output fra 033:

```
ALTER TABLE
```

Fjerner `product_code`, men trigger sætter stadig NEW.product_code, og new_writer
kommer derfor til at fejle, fordi `product_code` ikke eksisterer mere.

Output fra new_writer_step10.sql:

```
ERROR:  record "new" has no field "product_code"
CONTEXT:  PL/pgSQL assignment "NEW.product_code := (SELECT code FROM products WHERE id = NEW.product_id)"
PL/pgSQL function add_product_code() line 5 at assignment
```

Vi kan ikke droppe trigger før 033, for der findes `product_code` stadig og er NOT NULL,
hvis vi først dropper trigger efter 033, vil der være et vindue, hvor new_writer fejler.
Derfor burde trigger fjernes i samme transaktion som 033.

Dropper funktionen `add_product_code()`, det sletter også trigger. Hvis vi kun droppede trigger,
ville funktionen stadig eksisterer.

Kør: `drop_product_code_trigger.sql`

Output fra new_writer_step10.sql efter drop:

```
INSERT 0 1
```

Output fra: `select id, product_id from tickets where id = 'TICKET-10'`

```
- TICKET-10 - d361ee71-126e-479b-8760-ecff30faf420
```

### 11. Priser og valuta før/efter

- Starter fra tom db: `docker compose down -v` og `docker compose up -d`
- 011-022 køres automatisk fra `db/`
- Kører `baseline.sql`
- Kører `baseline_snapshot_table.sql` som vi skal bruge til at sammenligne

```
id - product_code - price - currency
TICKET-1 - SINGLE - 36.00 - DKK
TICKET-2 - SINGLE - 36.00 - DKK
TICKET-3 - DAY - 80.00 - DKK
```

- Kører 030-033
- Kører `compare_price_and_valuta.sql` som skal give 0 rows, det betyder at der ikke er forskel
  fra før 030 til efter 033.

Output:

```
 id 
----
(0 rows)
```


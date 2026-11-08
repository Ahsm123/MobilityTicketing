# MobilityTicketing

Lab-afleveringer til databasekurset. Domænet er det samme hele vejen: billetsalg til bybus, metro og tog.

Hver lab-mappe kører alene med sin egen `compose.yaml` og har en README med spørgsmål, konklusion, kørselsvejledning og evidence. Labs, der bygger videre på en tidligere lab, har den tidligere slutstand kopieret ind i `db/`.

| Lab | Spørgsmål | Stack |
|---|---|---|
| [01](./lab01-relational-model/) | Mindste relationelle model for ruter og afgange | PostgreSQL |
| [02](./lab02-integrity/) | Hvad kan databasen selv garantere? | PostgreSQL |
| [03](./lab03-reporting/) | Hvor skal reporting-logik køre? | PostgreSQL |
| [04](./lab04-product-migration/) | Skift product identity uden at bryde billetter | PostgreSQL |
| [05](./lab05-mongodb-search/) | Journey search som dokumenter | MongoDB |
| [06](./lab06-redis-cache/) | Cache-aside foran journey search | Redis, MongoDB, .NET |
| [07](./lab07-database-design/) | Live board, snapshots og bookings (i gang) | PostgreSQL, EF Core |
| [08](./lab08-query-optimization/) | Departure search og validation history (query optimization) | PostgreSQL |
| [09](./lab09-booking-concurrency/) | Sidste plads og idempotente retries ved samtidige bookings | PostgreSQL |

Fælles domænemodel: [`docs/model/`](./docs/model/) (dossier, access patterns, integrity map og diagrammer).

## Kørsel

Gå ind i lab-mappen og kør `docker compose up -d`. Lab 01–06 bruger de samme host-porte (5432, 8080, 27017, 6379), så kør én lab ad gangen, og stop den med `docker compose down -v`. Lab 09 eksponerer ingen host-port.

# Compulsory Assignment 1 review guide

Submitted commit: TODO

Setup and reset: [Kørsel](#kørsel), og afsnittet "Kør det" i hver labs README. Hver lab starter fra en tom database med `docker compose down -v` efterfulgt af `docker compose up -d`.

## Where to find the work

**Lecture 1: model, workload map and queries** ([lab01](./lab01-relational-model/))

- [access patterns](./docs/model/access-patterns.md), [ERD](./docs/model/diagrams/ERD_2.jpg), [context](./docs/model/diagrams/mobility-ticketing-context.jpg)
- [dossier](./docs/model/dossier.md) (antagelser og functional dependency)
- [schema](./lab01-relational-model/db/001_relational_baseline.sql), [seed](./lab01-relational-model/db/002_seed.sql), [queries](./lab01-relational-model/queries.sql)

**Lecture 2: constraints and tests** ([lab02](./lab02-integrity/))

- [integrity map](./docs/model/integrity-map.md)
- [011_ticketing_integrity](./lab02-integrity/migrations/011_ticketing_integrity.sql)
- [test_suite](./lab02-integrity/tests/test_suite.sql), [payment-retry](./lab02-integrity/payment-retry.sql)

**Lecture 3: reporting experiment and comparison** ([lab03](./lab03-reporting/))

- [evidence og responsibility matrix](./lab03-reporting/README.md)
- [base_revenue](./lab03-reporting/base_revenue.sql), [reporting_cases](./lab03-reporting/reporting_cases.sql)
- [020_reporting_function](./lab03-reporting/migrations/020_reporting_function.sql), [021_daily_revenue_trigger](./lab03-reporting/migrations/021_daily_revenue_trigger.sql), [022_daily_captured_revenue](./lab03-reporting/migrations/022_daily_captured_revenue.sql)

**Lecture 4: migration stages and verification** ([lab04](./lab04-product-migration/))

- [evidence](./lab04-product-migration/README.md), der går trin for trin gennem afsnit 1–11
- Baseline: [baseline](./lab04-product-migration/scripts/baseline.sql), [baseline_snapshot_table](./lab04-product-migration/scripts/baseline_snapshot_table.sql)
- Overlap: [030_expand](./lab04-product-migration/migrations/030_expand_product_identity.sql), [product_code_trigger](./lab04-product-migration/scripts/product_code_trigger.sql), [old_writer](./lab04-product-migration/scripts/old_writer_step4.sql), [old_reader](./lab04-product-migration/scripts/old_reader.sql), [new_writer](./lab04-product-migration/scripts/new_writer.sql), [new_reader](./lab04-product-migration/scripts/new_reader.sql)
- Backfill: [031_backfill](./lab04-product-migration/migrations/031_backfill_ticket_product.sql)
- Late ticket og checks: [old_writer_step8](./lab04-product-migration/scripts/old_writer_step8.sql), [verify](./lab04-product-migration/scripts/verify.sql)
- Contract: [032_require](./lab04-product-migration/migrations/032_make_product_id_required_reference.sql), [old_writer_step9](./lab04-product-migration/scripts/old_writer_step9.sql) (afvist), [033_drop](./lab04-product-migration/migrations/033_drop_product_code.sql), [drop_product_code_trigger](./lab04-product-migration/scripts/drop_product_code_trigger.sql), [new_writer_step10](./lab04-product-migration/scripts/new_writer_step10.sql)
- Pris og valuta: [compare_price_and_valuta](./lab04-product-migration/scripts/compare_price_and_valuta.sql) (0 rækker = uændret)

## Two decisions worth discussing

### 1. Reporting: function frem for derived copies

- **Valg:** [`captured_revenue_for_day(operator_id, date)`](./lab03-reporting/migrations/020_reporting_function.sql). Alle callers kører samme SQL direkte mod `payments`, så der findes ingen kopi, der kan blive stale.
- **Alternativ:** En materialized view giver billigere reads, men er stale indtil refresh. Det er acceptabelt, fordi reporting gerne må være eventual ifølge access patterns, så MV er næste skridt, hvis read cost bliver et problem. Refresh i en trigger på `payments` er dog en dårlig idé, fordi en fejlende refresh så ruller betalingen tilbage. Trigger-tabellen håndterer ikke statusskift, så den kan være forkert, uden at nogen opdager det.
- **Hvorfor:** `payments` er authority, reporting sker pr. operatør (det er funktionens parameter), og read cost er ikke et problem endnu.
- **Evidens:** [lab03 README](./lab03-reporting/README.md). Under Captured > Refunded viser MV 108.00/3 før refresh, mens den direkte query viser 72.00/2.

### 2. Partial unique index på payments

- **Valg:** `UNIQUE INDEX payments_external_reference_unique ON payments (external_payment_reference) WHERE status IN ('Captured', 'Refunded')` i [011_ticketing_integrity](./lab02-integrity/migrations/011_ticketing_integrity.sql).
- **Alternativ:** a) en almindelig `UNIQUE` på kolonnen, eller b) ingen constraint og et check i appen.
- **Hvorfor:** Hvis en gateway sender den samme betaling to gange, må det ikke give dobbelt revenue. En fejlet betaling skal kunne retries med samme reference, og det kan a) ikke. Med b) kan der opstå en race condition, når to betalinger kommer samtidig.
- **Evidens:** I [payment-retry](./lab02-integrity/payment-retry.sql) accepteres `Failed` og en retry med `Captured`, mens det andet `Captured` afvises med `23505`. Det samme ses i [lab03](./lab03-reporting/README.md) state 6.

## One limitation or open question

### Ticket validation

En SINGLE-billet kan valideres flere gange. `validations` har kun FK og et check på `result`, så intet forhindrer to accepted rækker for den samme billet. Antallet af tilladte valideringer afhænger af produktet: SINGLE én gang, DAY flere gange. I tog skal en kontrollør desuden kunne validere on demand. En constraint på tabelniveau kender hverken produkttypen eller transportmidlet.

- **Evidens:** [integrity map](./docs/model/integrity-map.md), regel 12, og [011_ticketing_integrity](./lab02-integrity/migrations/011_ticketing_integrity.sql).
- **Next:** Lad en SINGLE-billet skifte fra `Active` til `Validated`, men kun hvis den er `Active`, så en allerede brugt billet bliver opdaget. Afklar derefter, om DAY-reglen hører hjemme i databasen eller i den app, der validerer.

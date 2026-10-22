# Lab 02: Integrity and payment retry

## Spørgsmål

Hvad kan databasen selv garantere, når writes kommer fra forskellige apps og scripts?

## Konklusion

- 13 regler er klassificeret og dokumenteret i [integrity map](../docs/model/integrity-map.md), og [dossier](../docs/model/dossier.md) indeholder reglerne og A–D-klassifikationen.
- Valg: partial unique index `payments_external_reference_unique ON payments (external_payment_reference) WHERE status IN ('Captured', 'Refunded')`. En gateway, der sender den samme betaling to gange, kan ikke give dobbelt revenue, og en `Failed` betaling kan stadig retries med samme reference. Almindelig `UNIQUE` ville blokere retry, og et check i appen giver en race condition.
- Åbent spørgsmål: en SINGLE-billet kan valideres flere gange (regel 12). Antal tilladte valideringer afhænger af produkt og transportmiddel, så det kan ikke løses med en simpel constraint.

## Kør det

```powershell
docker compose up -d
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/migrations/011_ticketing_integrity.sql
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/tests/test_suite.sql
docker compose exec -T postgres psql -U mobility -d mobility -f /lab/payment-retry.sql
docker compose down -v
```

- Test suite: alle 13 linjer printer `ok:`.
- Payment retry: `Failed` og retry med `Captured` accepteres, mens det andet `Captured` afvises med `23505`.

## Filer

- `db/`: baseline fra lab 01 plus den svage starter-DDL (`010_ticketing_draft.sql`, `011_ticketing_seed.sql`)
- `migrations/011_ticketing_integrity.sql`: constraints
- `tests/test_suite.sql`: negative og positive tests med SQLSTATE
- `payment-retry.sql`: retry-scenariet

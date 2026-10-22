# Lab 02: Integrity and payment retry

## Formål

At forbedre dataintegriteten i det oprindelige postgresql schema i forhold til:
* ticket purchase
* validation

Fokus er på hvilke garantier en database kan give, når writes kan komme fra forskellige klienter.

## Konklusion

- 13 regler er klassificeret og dokumenteret i [integrity map](../docs/model/integrity-map.md), og [dossier](../docs/model/dossier.md) indeholder reglerne og A–D klassifikationen.

- Til at sikre, at en betaling ikke bliver trukket mere end én gang, bruges et partial unique index `payments_external_reference_unique ON payments (external_payment_reference) WHERE status IN ('Captured', 'Refunded')`. Hvis en gateway sender den samme betaling to gange, kan det ikke give dobbelt revenue, og en `Failed` betaling kan stadig prøves igen med samme reference. En almindelig `UNIQUE` ville blokere retry, og et check i appen giver en race condition.

- En SINGLE-billet kan valideres flere gange (regel 12). Antal tilladte valideringer afhænger af produkt og transportmiddel, så det kan ikke løses med en simpel constraint.

## Run

```powershell
docker compose up -d
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/migrations/011_ticketing_integrity.sql
docker compose exec -T postgres psql -U mobility -d mobility -v ON_ERROR_STOP=1 -f /lab/tests/test_suite.sql
docker compose exec -T postgres psql -U mobility -d mobility -f /lab/payment-retry.sql
docker compose down -v
```

- Test suite: alle 13 linjer skal være `ok:`.
- Payment retry: `Failed` og retry med `Captured` bliver accepteret, mens det andet `Captured` bliver afvist med `23505`.

## Filer

- `db/`: baseline fra lab 01 og starter DDL (`010_ticketing_draft.sql`, `011_ticketing_seed.sql`)
- `migrations/011_ticketing_integrity.sql`: constraints.
- `tests/test_suite.sql`: negative og positive tests med SQLSTATE.
- `payment-retry.sql`: bevis for retry scenariet.

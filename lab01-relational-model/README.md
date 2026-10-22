# Lab 01: Relational model

## Formål

Implementer den mindste relationelle model som understøtter:
* route maintaince
* upcoming-trip queries


## Konklusion

- `route_stops` har nøglen `(route_id, stop_sequence)`, fordi en rute kan indeholde det samme stop flere gange (aldrig to gange i træk). FD: `route_id, stop_sequence -> stop_id`. Forespørgslen på ordered stops bruger netop den nøgle: den filtrerer på `route_id` og sorterer på `stop_sequence`, og `stop_id` joines til `stops` for at få navnet, så navnet ikke gentages i `route_stops`.
- Antagelse der kan ændre sig: i bus og tram validerer kunden selv ved påstigning, mens en kontrollør validerer on demand i tog.
- Model, access patterns og ERD: [dossier](../docs/model/dossier.md), [access patterns](../docs/model/access-patterns.md), [ERD](../docs/model/diagrams/ERD_2.jpg), [context](../docs/model/diagrams/mobility-ticketing-context.jpg).

## Kør det

```powershell
docker compose up -d
docker compose exec -T postgres psql -U mobility -d mobility -f /lab/queries.sql
docker compose down -v
```

`db/` køres automatisk på en tom database. pgweb: <http://localhost:8080>.

## Filer

- `db/001_relational_baseline.sql`: schema
- `db/002_seed.sql`: seed
- `queries.sql`: de tre workload queries

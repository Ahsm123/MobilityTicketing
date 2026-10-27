# Lab 07: Database design (live board, snapshots, bookings)

## Spørgsmål

Hvordan læser man det samme data på tre måder, current, preserved og checked, uden at de forstyrrer hinanden?

Live board skal vise opdaterede oplysninger om afgangene. Dispatch skal samtidig have gemt en kopi, hver gang der bliver
publiceret en køreplan.
Bookingsystemet skal altid kontrollere, om afgangen stadig kører, også selvom passageren ser på forældet data på live
board.

| Del            | Krav                                                                 | Type      |
|----------------|----------------------------------------------------------------------|-----------|
| Live board     | Show current route names, scheduled-trip counts and next departures. | Current   |
| Published plan | Keep the values shown when the plan was published.                   | Preserved |
| Booking        | Check the current trip status and handle repeated requests.          | Checked   |

## Konklusion

- **Live board:** Projection til dto'en `RouteOverviewDto` direkte i én query. Det giver 1 command og 0 materialized
  entities, uanset om det er 4 ruter og 9 trips eller 200 ruter og 100K trips. Baseline med skalering var 201 commands
  og 100.200 entities. Se [evidence](#1-live-board-loading-strategi).
- **Request (A) frem for summary table (B):** kontrakten kræver vilkårlige vinduer op til 24 timer, så B skal alligevel
  beregne. A har én source of truth. Se [design](docs/design.md).
- **Published plan:** header- og detail-rækker med kopierede værdier, så et rename eller cancel ikke ændrer planen. Read
  er én projection med en collection, så header og rows hentes i én query med JOIN. Se [design](docs/design.md).
- **Commands:** hver command kører i én transaktion med en advisory lock på en key. Booking bruger `request:{id}`, så
  retries af samme request venter på hinanden. Request-ID'et tjekkes før trip-status, så en retry får sin receipt, også
  efter en cancel. Publish bruger plan-ID'et, og samme ID med et andet vindue kaster `SnapshotConflictException`.

## Kør det

```powershell
docker compose up -d --wait postgres
docker compose build app tests
docker compose run --rm app reset
docker compose run --rm app overview
docker compose run --rm tests
docker compose down -v
```

`reset` genskaber `lab07` (4 ruter, 9 trips, standardvindue 23/10 2026 06:00–07:00 UTC). `scale` giver 200 ruter og
100.000 trips, og `baseline` kører starterens lazy loading til sammenligning. Kommandoerne er beskrevet
i [kontrakten](docs/contract.md). pgweb: <http://localhost:8082>.

## Evidence

### 1. Live board: loading-strategi

| Data size                  | Read       | Executed commands | Route entities | Trip entities |
|----------------------------|------------|------------------:|---------------:|--------------:|
| 4 routes / 9 trips         | Baseline   |                 5 |              4 |             9 |
| 4 routes / 9 trips         | Live board |                 1 |              0 |             0 |
| 200 routes / 100,000 trips | Baseline   |               201 |            200 |        100000 |
| 200 routes / 100,000 trips | Live board |                 1 |              0 |             0 |

Baseline er 1+N: én query for ruterne og én pr. rute for dens trips, og filtreringen sker i C#. Projection beregner
`count` og `min` i databasen. SQL og output er linket i [evidence-tabellen](docs/evidence/lecture07/README.md).

### 2. Cancel, rename, booking og snapshot

Kørt efter `reset`, 27/10 2026:

| Step                          | Resultat                                             | Exit |
|-------------------------------|------------------------------------------------------|-----:|
| `overview`                    | R1 City Loop, 2 trips, næste 06:00                   |    0 |
| `publish morning`             | `Published`                                          |    0 |
| `book T201 campus-1`          | `Booked`, receipt 07:09:58.393672Z                   |    0 |
| `book T201 campus-1`          | `AlreadyBooked`, samme receipt                       |    0 |
| `cancel T102`                 | `Cancelled`                                          |    0 |
| `rename R1 "City Ring"`       | `Renamed`                                            |    0 |
| `book T102 cancelled-1`       | `Cancelled`, ingen receipt                           |    2 |
| `overview`                    | R1 **City Ring**, **1** trip, næste **06:20**        |    0 |
| `snapshot morning`            | R1 **City Loop**, **2** trips, næste **06:00**       |    0 |
| `publish revised`             | `Published`                                          |    0 |
| `snapshot revised`            | R1 City Ring, 1 trip, næste 06:20                    |    0 |
| `publish morning` 07:00–08:00 | `Snapshot 'morning' belongs to another time window.` |    1 |

Live board og `morning` viser forskellige værdier for samme vindue. Live board er current, og `morning` er preserved.
`revised` er publiceret efter ændringerne og har dem med.

Logs: [overview før](docs/evidence/lecture07/logs/20261027T0709460379080Z-live-ee1baadf.sql), [overview efter](docs/evidence/lecture07/logs/20261027T0710210397339Z-live-616b2a40.sql), [snapshot morning](docs/evidence/lecture07/logs/20261027T0710255041039Z-snapshot-4fd000f9.sql), [snapshot revised](docs/evidence/lecture07/logs/20261027T0710345363036Z-snapshot-f2bfcd91.sql).

### 3. Snapshot read: ét read

```sql
SELECT t.from_utc,
       t.to_utc,
       t.published_at_utc,
       t.plan_id,
       s0.route_id,
       s0.route_name,
       s0.count,
       s0.next_departure_utc,
       s0.plan_id
FROM (SELECT s.from_utc, s.to_utc, s.published_at_utc, s.plan_id
      FROM lab07.snapshot_header AS s
      WHERE s.plan_id = @__snapshotId_0
    LIMIT 2) AS t
         LEFT JOIN lab07.snapshot_rows AS s0 ON t.plan_id = s0.plan_id
ORDER BY t.plan_id, s0.route_id
```

`ExecutedReadCommands: 1`, `MaterializedRoutes: 0`, `MaterializedTrips: 0`. Route-navnet kommer fra `snapshot_rows`,
ikke fra `routes`, ellers ville et rename ændre planen.

### 4. Tests

`docker compose run --rm tests`: 47/47 grønne, 27/10 2026. Det dækker bl.a. otte samtidige retries af booking og
publish (1 `Booked`/`Published` og 7 `AlreadyBooked`/`AlreadyPublished`) og en hook, der kaster før commit og ikke
efterlader nogen snapshot. [TRX](docs/evidence/lecture07/tests/_bf64ee40ff2f_2026-10-27_06_54_02.trx).

## Åbent

- `StudentTests.cs` er tom. Kontrakten kræver mindst fire egne tests.

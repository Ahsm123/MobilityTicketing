# Lab 09: Booking concurrency (sidste plads og retries)

Status: ikke startet.

## Spørgsmål

Hvordan booker man den sidste plads korrekt, når to sessions booker samtidig, og en retry med samme `request_id` skal returnere den oprindelige booking i stedet for at oprette en ny?

## Kør det

PostgreSQL 17 environment, fixture data, SQL scripts and a booking-function stub.
Requires Docker with Compose. Run shell commands from this directory.

### Start

```sh
docker compose up -d --wait postgres
docker compose exec -T postgres bash /lab/run.sh reset
```

The database is `mobility_l09`, with user and password `mobility`. It runs inside
the container. No host port or local PostgreSQL installation is required.

### Commands

```sh
docker compose exec -T postgres bash /lab/run.sh load
docker compose exec -T postgres bash /lab/run.sh inspect
docker compose exec -T postgres bash /lab/run.sh check
```

| Command | Effect |
| --- | --- |
| `reset` | Recreates `lab09`, restores fixture data and loads `sql/book.sql`. |
| `load` | Loads `sql/book.sql` without resetting data. |
| `inspect` | Prints trip counters and booking counts. |
| `check` | Runs the supplied checks. Temporary fixture changes are rolled back. |

`reset` does not change files on your computer. End open database transactions
before resetting or running checks. The checks fail while `sql/book.sql` contains
the initial stub.

### Open a database session
```sh
docker compose exec postgres psql -P pager=off -X -U mobility -d mobility_l09 -v ON_ERROR_STOP=1 -v VERBOSITY=verbose -v "PROMPT1=[%p]%x%# "
```

Run this command in each terminal when multiple sessions are needed. At the
`psql` prompt:

```sql
\pset pager off
```

Use `\i /lab/sql/filename.sql` to run a SQL file. Files in this directory are
mounted at `/lab` inside the container. Use `\q` to exit. After an error inside a
transaction, run `ROLLBACK;` before starting another transaction.

Live view of sessions, trips and bookings (own terminal, refreshes every second;
stop with Ctrl+C before closing the window):

```sh
docker compose exec postgres bash -c "while :; do clear; psql -XqU mobility -d mobility_l09 -P pager=off -f /lab/watch.sql; sleep 1; done"
```

### Files

| Path | Contents |
| --- | --- |
| `compose.yaml` | PostgreSQL container and persistent data volume. |
| `database/setup.sql` | Schema and fixture data. |
| `sql/book.sql` | Booking-function stub loaded by `reset` and `load`. |
| `sql/unsafe-read.sql` | Reads fixture data into psql variables. |
| `sql/unsafe-write.sql` | Uses variables populated by the read script. |
| `sql/inspect.sql` | Database inspection query. |
| `sql/check.sql` | Supplied function checks. |
| `sql/variants/` | Empty directory for additional SQL files. |
| `docs/evidence.md` | Empty notes file. |

The unsafe scripts require psql variables named `trip_id`, `request_id` and
`passenger_id`. Set them with `\set variable_name value`. Run the read script
before the write script in the same session. They do not begin or end a transaction.

The function signature is:

```sql
lab09.book(p_request_id text, p_passenger_id text, p_trip_id text) RETURNS bigint
```

### Save output

From a shell:

```sh
docker compose exec -T postgres bash /lab/run.sh inspect > docs/result.txt
```

### Stop and troubleshoot

```sh
docker compose stop
docker compose logs postgres
```

Stopping preserves the database volume. If a query waits, check for an unfinished
transaction in another session. The supplied checks use a five-second lock timeout.

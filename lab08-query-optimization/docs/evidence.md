# Query optimization notes

Use the `Execution Time` from each of the five measured plans. Sort the five values to find the median. Record the
smallest and largest as the range.

| Route | Query / active index        | Median ms | Range ms              | Scan / filter / sort observation                                                            | Top-node shared hit / read buffers | Check passes? |
|-------|-----------------------------|-----------|-----------------------|---------------------------------------------------------------------------------------------|------------------------------------|---------------|
| R-7   | Baseline / primary key only | 6.436 ms  | 6.200 ms - 7.393 ms   | Seq scan on trips, filter routeid > status > date<br/>Quick sort on departure_utc + id      | hit: 1007, read: 0                 | Yes           |
| R-7   | Rewrite / primary key only  | 6.079 ms  | 5.786 ms - 6.813 ms   | Seq Scan on trips, filter date > routeid > status<br/>Quick sort on departure_utc + id      | hit: 1007, read: 0                 | Yes           |
| R-7   | Rewrite / route-only index  | 1.421 ms  | 1.405 ms - 1.614 ms   | Bitmap scan on trips_route_id, filter date > status<br/>Quick sort on departure_utc + id    | hit: 1011, read: 0                 | Yes           |
| R-7   | Rewrite / chosen index      | 0.113 ms  | 0.111 ms - 0.115 ms   | Index Scan on partial index, no filter<br/>Incremental Sort, departure presorted            | hit: 33, read: 0                   | Yes           |
| R-7   | Rewrite / AI index          | 0.052 ms  | 0.048 ms - 0.076 ms   | Index Only Scan, no filter, no sort, Heap Fetches: 0                                        | hit: 4, read: 0                    | Yes           |
| R-7   | Rewrite / partial + id      | 0.102 ms  | 0.086 ms - 0.117 ms   | Index Scan on partial + id index, no filter, no sort                                        | hit: 23, read: 0                   | Yes           |
| R-0   | Baseline / primary key only | 13.966 ms | 13.665 ms - 15.470 ms | Seq Scan on trips, filter routeid > status > date <br/>Top-N heapsort on departure_utc + id | hit: 1007, read: 0                 | Yes           |
| R-0   | Rewrite / primary key only  | 6.349 ms  | 6.114 ms - 6.859 ms   | Seq Scan on trips, filter date > routeid > status<br/>Top-N heapsort on departure_utc + id  | hit: 1007, read: 0                 | Yes           |
| R-0   | Rewrite / route-only index  | 6.290 ms  | 5.804 ms - 6.808 ms   | Seq Scan, index unused, filter as rewrite<br/>Top-N heapsort on departure_utc + id          | hit: 1007, read: 0                 | Yes           |
| R-0   | Rewrite / chosen index      | 0.119 ms  | 0.118 ms - 0.120 ms   | Index Scan on partial index, no filter<br/>Incremental Sort, departure presorted            | hit: 34, read: 0                   | Yes           |
| R-0   | Rewrite / AI index          | 0.048 ms  | 0.046 ms - 0.067 ms   | Index Only Scan, no filter, no sort, Heap Fetches: 0                                        | hit: 4, read: 0                    | Yes           |
| R-0   | Rewrite / partial + id      | 0.092 ms  | 0.091 ms - 0.099 ms   | Index Scan on partial + id index, no filter, no sort                                        | hit: 24, read: 0                   | Yes           |

## Keep or remove?

- Index sizes: route-only 832 kB; chosen index 3440 kB; AI index 5840 kB; partial + id 4408 kB
- Index to keep: Partial + id, `trips_partial_routeid_departure_id_scheduled ON lab08.trips (route_id,
  scheduled_departure_utc, id) WHERE status = 'Scheduled'`.
- One result that supports the decision: Index Scan uden Filter og uden Sort. Median 0,102 ms (R-7) og 0,092 ms (R-0),
  23 / 24 buffers mod 1007 i baseline. `Limit` stopper efter 20 rækker.
- One alternative rejected, and why: AI-indexet `(route_id, status, scheduled_departure_utc, id)`. Det var hurtigst
  (0,052 / 0,048 ms, 4 buffers, Index Only Scan), men det indeholder også aflyste trips og senere afgåede trips. Hvis
  95 % af rækkerne en dag er Departed, vokser indexet med hele tabellen (5840 kB allerede nu), mens partial + id kun
  indeholder de planlagte afgange (4408 kB). Gevinsten er kun cirka 0,05 ms.
- What changes when a scheduled departure is inserted, rescheduled or cancelled? Insert: en ny indgang, men kun hvis
  status er Scheduled. Reschedule ændrer `scheduled_departure_utc`, som står i indexet, så updaten er ikke HOT, og der
  skrives en ny indgang. Cancel ændrer `status`, som står i prædikatet, så updaten er heller ikke HOT. Den nye
  rækkeversion kommer ikke ind i indexet, og den gamle indgang ryddes op af VACUUM.
- Your AI prompt:

  ````text
  I have a PostgreSQL 17 query that i want to speed up. Suggest ONE index
  and explain how it handles the route filter, ordering and the status filter.
  Also say what it costs when a trip is inserted, rescheduled or cancelled.

  Query (route_id, query_day and result_limit are psql variables, limit is 20):

  select id, route_id, scheduled_departure_utc, status
  from lab08.trips
  where route_id = :'route_id'
    and status = 'Scheduled'
    and scheduled_departure_utc >= :'query_day'::date::timestamp at time zone 'UTC'
    and scheduled_departure_utc < (:'query_day'::date + 1)::timestamp at time zone 'UTC'
  order by scheduled_departure_utc, id
  limit :result_limit;

  Table:

  create table lab08.trips (
      id bigint primary key,
      route_id text not null,
      scheduled_departure_utc timestamptz not null,
      status text not null check (status in ('Scheduled', 'Cancelled'))
  );

  Data: 120,008 trips over 3 months. Route R-0 has ~75% of the rows, R-7 ~3%.
  About 1 in 13 trips is Cancelled.

  Existing indexes:

   indexname  |                            indexdef                            |  size
  ------------+----------------------------------------------------------------+---------
   trips_pkey | CREATE UNIQUE INDEX trips_pkey ON lab08.trips USING btree (id) | 2640 kB

  Baseline plan for R-7:

   Limit  (cost=3701.50..3701.54 rows=16 width=30) (actual time=6.177..6.180 rows=20 loops=1)
     Buffers: shared hit=1007
     ->  Sort  (cost=3701.50..3701.54 rows=16 width=30) (actual time=6.176..6.177 rows=20 loops=1)
           Sort Key: scheduled_departure_utc, id
           Sort Method: quicksort  Memory: 27kB
           Buffers: shared hit=1007
           ->  Seq Scan on trips  (cost=0.00..3701.18 rows=16 width=30) (actual time=0.179..6.137 rows=38 loops=1)
                 Filter: ((route_id = 'R-7'::text) AND (status = 'Scheduled'::text) AND (((scheduled_departure_utc AT TIME ZONE 'UTC'::text))::date = '2026-10-23'::date))
                 Rows Removed by Filter: 119970
                 Buffers: shared hit=1001
   Planning:
     Buffers: shared hit=97
   Planning Time: 0.203 ms
   Execution Time: 6.200 ms
  ````

  AI forslag:
  `CREATE INDEX trips_route_status_departure_idx ON lab08.trips (route_id, status, scheduled_departure_utc, id);`
- One AI suggestion accepted, changed or rejected, and the check that decided it: Ændret. Jeg beholdt AI'ens idé med
  `id` sidst, så hele ORDER BY ligger i indexet, men lavede `status` om til et partial-prædikat i stedet for en kolonne.
  `check` gav 4 × PASS, og planen viste, at Sort var væk (Index Scan, rows=20).

## Partitioning

| Read               | Plain-table plan                                                                                                 | Partitions actually accessed                                                                                                | Same result?                   |
|--------------------|------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------|--------------------------------|
| October count      | Index Only Scan on validations_plain_pkey, Index Cond on validated_utc, Heap Fetches: 0<br/>5.211 ms, hit: 198   | validations_oct only (Seq Scan, no Append)<br/>4.820 ms, hit: 261                                                           | Yes, 40889 / 40889             |
| Ticket 100 history | Seq Scan, Filter ticket_id, Rows Removed by Filter: 119988<br/>Sort on validated_utc + id<br/>5.035 ms, hit: 771 | validations_sep, validations_oct, validations_nov (Append over all three, each Seq Scan with Filter)<br/>5.167 ms, hit: 766 | Yes, 12 / 12, 0 different rows |

Would you choose monthly partitions for reporting, ticket support, or monthly retention? Refer to the plans.

- Rapport: Pruning virker, for planen læser kun `validations_oct`. Men den er ikke hurtigere end den almindelige tabel.
  Dér står `validated_utc` forrest i primary key'en `(validated_utc, id)`, så en Index Only Scan kan allerede nøjes med
  at læse oktober.
- Ticket support: Ingen pruning. `ticket_id` er ikke partitionsnøglen, så alle tre partitioner scannes. Det er det
  samme arbejde som den almindelige Seq Scan. Det kræver et index på `ticket_id`, oprettet på parent-tabellen, så alle
  partitioner får det.
- Retention: Det er grunden til at partitionere. En måned kan fjernes med `DROP TABLE` eller `DETACH PARTITION` i stedet
  for en stor `DELETE` efterfulgt af `VACUUM`.

Valg: Ja, månedspartitioner nu. 120.000 rækker er ikke meget, men validations vokser hele tiden. Senere kan en
almindelig tabel ikke laves om til en partitioneret uden at flytte alle data. Pris: Primary key'en skal indeholde
`validated_utc`. Reads uden partitionsnøglen læser alle partitioner. Og næste måneds partition skal findes, før dens
data kommer.

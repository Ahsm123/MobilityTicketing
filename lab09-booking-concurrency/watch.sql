-- Live view for lab09. Run in its own terminal:
--   docker compose exec postgres bash -c "while :; do clear; psql -XqU mobility -d mobility_l09 -P pager=off -f /lab/watch.sql; sleep 1; done"
-- This watcher is its own session: it sees COMMITTED data only.
-- A row whose xmax equals a session's xid is locked/updated by that open transaction.

\echo '=== SESSIONS (dine psql-terminaler) ==='
SELECT pid,
       state,
       backend_xid AS xid,
       CASE WHEN xact_start IS NOT NULL
            THEN round(extract(epoch FROM now() - xact_start))::int || 's' END AS tx_open,
       CASE WHEN wait_event_type = 'Lock' THEN 'WAITING' END AS waiting,
       pg_blocking_pids(pid) AS blocked_by,
       left(regexp_replace(query, '\s+', ' ', 'g'), 70) AS last_query
FROM pg_stat_activity
WHERE datname = current_database()
  AND backend_type = 'client backend'
  AND pid <> pg_backend_pid()
ORDER BY pid;

\echo '=== TRIPS (committed) ==='
SELECT t.xmin, t.xmax, t.id, t.status, t.capacity, t.reserved_seats,
       (SELECT count(*) FROM lab09.bookings b WHERE b.trip_id = t.id) AS bookings,
       t.reserved_seats = (SELECT count(*) FROM lab09.bookings b WHERE b.trip_id = t.id) AS agree
FROM lab09.trips t
ORDER BY t.id;

\echo '=== BOOKINGS (committed) ==='
SELECT xmin, id, request_id, passenger_id, trip_id
FROM lab09.bookings
ORDER BY id;

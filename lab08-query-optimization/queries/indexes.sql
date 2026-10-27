-- Write your experimental DROP INDEX and CREATE INDEX statements here.
-- Test one candidate at a time. Drop only indexes you created for this experiment.
-- The primary-key index is part of the starting schema and must stay.

DROP INDEX IF EXISTS lab08.trips_route_id;
-- CREATE INDEX trips_route_id ON lab08.trips (route_id);

-- Det meste tid bliver brugt i Sort, fordi den skal scanne alle rækker inden den sorterer.
-- Hvis det var sorteret i forvejen, ville limit stoppe efter de 20 rækker.
-- Et partial index med route_id, departure, status = Scheduled:
DROP INDEX IF EXISTS lab08.trips_partial_routeid_departure_scheduled;
-- CREATE INDEX trips_partial_routeid_departure_scheduled
--     ON lab08.trips (route_id, scheduled_departure_utc) WHERE status = 'Scheduled';

-- AI forslag
DROP INDEX IF EXISTS lab08.trips_route_status_departure_idx;
-- CREATE INDEX trips_route_status_departure_idx
--     ON lab08.trips (route_id, status, scheduled_departure_utc, id);

-- Partial index med id sidst, så hele ORDER BY ligger i indexet
DROP INDEX IF EXISTS lab08.trips_partial_routeid_departure_id_scheduled;
CREATE INDEX trips_partial_routeid_departure_id_scheduled
    ON lab08.trips (route_id, scheduled_departure_utc, id) WHERE status = 'Scheduled';


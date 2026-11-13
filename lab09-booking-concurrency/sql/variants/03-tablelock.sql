-- Your implementation goes here. Run `bash /lab/run.sh load` after editing.
-- The function runs in the caller's transaction; do not COMMIT inside it.
CREATE OR REPLACE FUNCTION lab09.book(
    p_request_id text,
    p_passenger_id text,
    p_trip_id text
)
RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    v_trip_id    text;
    v_booking_id bigint;
    v_seen_reserved int;
    v_capacity int;
BEGIN
    LOCK TABLE lab09.trips IN EXCLUSIVE MODE;
    SELECT reserved_seats, capacity
        INTO v_seen_reserved, v_capacity
    FROM lab09.trips
    WHERE id = p_trip_id and status = 'Scheduled';
    
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Trip not found %', p_trip_id;
    END IF;
    
    IF v_seen_reserved >= v_capacity THEN
        RAISE EXCEPTION 'No seat available on trip %', p_trip_id;
    END IF;

    UPDATE lab09.trips
    SET reserved_seats = reserved_seats + 1
    WHERE id = p_trip_id
    RETURNING id INTO v_trip_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No seat available on trip %', p_trip_id;
    END IF;

    INSERT INTO lab09.bookings (request_id, passenger_id, trip_id)
    VALUES (p_request_id, p_passenger_id, v_trip_id)
    RETURNING id INTO v_booking_id;

    RETURN v_booking_id;
END;
$$;

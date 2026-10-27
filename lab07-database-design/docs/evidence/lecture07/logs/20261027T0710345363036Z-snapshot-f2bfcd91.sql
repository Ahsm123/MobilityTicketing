-- Parameters: {"@__snapshotId_0":"revised"}
SELECT t.from_utc, t.to_utc, t.published_at_utc, t.plan_id, s0.route_id, s0.route_name, s0.count, s0.next_departure_utc, s0.plan_id
FROM (
    SELECT s.from_utc, s.to_utc, s.published_at_utc, s.plan_id
    FROM lab07.snapshot_header AS s
    WHERE s.plan_id = @__snapshotId_0
    LIMIT 2
) AS t
LEFT JOIN lab07.snapshot_rows AS s0 ON t.plan_id = s0.plan_id
ORDER BY t.plan_id, s0.route_id
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Contracts;

namespace MobilityTicketing.Lecture07.Commands;

public static class CancelTripCommand
{
    public static Task<CancelResult> ExecuteAsync(string connection, string tripId, CancellationToken ct)
    {
        DatabaseSession.RequireText(tripId, "Trip ID");

        return DatabaseSession.WriteAsync(connection, $"trip:{tripId}", async db =>
        {
            var trip = await DatabaseSession.LockTripAsync(db, tripId, ct);
            if (trip is null)
                return CancelResult.NotFound;
            if (trip.Status == "Cancelled")
                return CancelResult.AlreadyCancelled;

            trip.Status = "Cancelled";
            return CancelResult.Cancelled;
        }, ct);
    }
}

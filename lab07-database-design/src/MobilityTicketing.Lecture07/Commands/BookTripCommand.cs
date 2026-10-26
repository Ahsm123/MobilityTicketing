using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Contracts;

namespace MobilityTicketing.Lecture07.Commands;

public static class BookTripCommand
{
    public static Task<BookingResult> ExecuteAsync(string connection, string tripId, string requestId, TimeProvider clock, CancellationToken ct)
    {
        // Valider inputtet
        DatabaseSession.RequireText(tripId, "Trip ID");
        DatabaseSession.RequireText(requestId, "Request ID");

        // Start transactionen med request key, så retries af samme request venter på hinanden
        return DatabaseSession.WriteAsync(connection, $"request:{requestId}", async db =>
        {
            // Retry? Return den gemte receipt, også hvis trip er cancelled siden
            var booking = await db.Bookings.SingleOrDefaultAsync(b => b.RequestId == requestId, ct);
            if (booking is not null)
            {
                if (booking.TripId != tripId)
                    return new BookingResult(BookingStatus.RequestConflict);

                return new BookingResult(BookingStatus.AlreadyBooked,
                    new BookingReceipt(booking.RequestId, booking.TripId, booking.BookedAtUtc));
            }

            var trip = await DatabaseSession.LockTripAsync(db, tripId, ct);
            if (trip is null)
                return new BookingResult(BookingStatus.NotFound);
            if (trip.Status == "Cancelled")
                return new BookingResult(BookingStatus.Cancelled);

            // mikrosekunder, fordi Postgres ikke matcher ellers
            var now = clock.GetUtcNow().UtcDateTime;
            var bookedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

            db.Bookings.Add(new Booking { RequestId = requestId, TripId = tripId, BookedAtUtc = bookedAt });
            return new BookingResult(BookingStatus.Booked, new BookingReceipt(requestId, tripId, bookedAt));
        }, ct);
    }
}

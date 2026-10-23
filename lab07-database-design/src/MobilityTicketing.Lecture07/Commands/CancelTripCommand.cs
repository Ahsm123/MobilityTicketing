using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Contracts;

namespace MobilityTicketing.Lecture07.Commands;

public class CancelTripCommand
{
    public Task<CancelResult> CancelTripAsync(string tripId, CancellationToken ct = default)
    {



        var trip = await db.Trips.FirstOrDefaultAsync(tripId, ct);
        if(trip is null)
            return new CancelResult.NotFound;
        

        if(trip.Status is "Cancelled")
            return new CancelResult.AlreadyCancelled;

    
    }
}
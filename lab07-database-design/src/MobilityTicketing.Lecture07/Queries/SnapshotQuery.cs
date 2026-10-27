using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Contracts;

namespace MobilityTicketing.Lecture07.Queries;

public static class SnapshotQuery
{
    public static async Task<PublishedSnapshot> ExecuteAsync(LectureDbContext db, string snapshotId, CancellationToken ct)
    {
        // Collection i en projection giver 1 query med JOIN = 1 read
        var plan = await db.SnapshotHeaders
            .Where(h => h.PlanId == snapshotId)
            .Select(h => new
            {
                h.FromUtc, h.ToUtc, h.PublishedAt,
                Rows = db.SnapshotRows
                    .Where(r => r.PlanId == h.PlanId)
                    .OrderBy(r => r.RouteId)
                    .Select(r => new RouteOverviewDto(r.RouteId, r.RouteName, r.Count, r.NextDeparture))
                    .ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (plan is null)
            throw new SnapshotNotFoundException(snapshotId);

        return new PublishedSnapshot(snapshotId, new TimeWindow(plan.FromUtc, plan.ToUtc), plan.PublishedAt, plan.Rows);
    }
}

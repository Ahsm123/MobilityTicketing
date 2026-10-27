using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Queries;

namespace MobilityTicketing.Lecture07.Commands;

public static class PublishSnapshotCommand
{
    public static Task<PublishResult> ExecuteAsync(string connection, string snapshotId, TimeWindow window,
        TimeProvider clock, Action? beforeCommit, CancellationToken ct)
    {
        DatabaseSession.RequireText(snapshotId, "Snapshot ID");
        window.Validate();
        
        return DatabaseSession.WriteAsync(connection, $"snapshot:{snapshotId}", async db =>
        {
            // Samme vindue er AlreadyPublished
            var header = await db.SnapshotHeaders.SingleOrDefaultAsync(h => h.PlanId == snapshotId, ct);
            if (header is not null)
            {
                if (header.FromUtc != window.FromUtc || header.ToUtc != window.ToUtc)
                    throw new SnapshotConflictException(snapshotId);

                return PublishResult.AlreadyPublished;
            }

            // Kopier værdier i samme transaktion
            var routes = await LiveBoardQuery.ExecuteAsync(db, window.FromUtc, window.ToUtc, ct);

            var now = clock.GetUtcNow().UtcDateTime;
            var publishedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
            
            db.SnapshotHeaders.Add(new SnapshotHeader
            {
                PlanId = snapshotId, FromUtc = window.FromUtc, ToUtc = window.ToUtc, PublishedAt = publishedAt
            });
            await db.SaveChangesAsync(ct);

            db.SnapshotRows.AddRange(routes.Select(r => new SnapshotRow
            {
                PlanId = snapshotId, RouteId = r.RouteId, RouteName = r.RouteName,
                Count = r.ScheduledTripCount, NextDeparture = r.NextDepartureUtc
            }));
            return PublishResult.Published;
        }, ct, beforeCommit);
    }
}

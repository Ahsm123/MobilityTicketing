using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

public static class RenameRouteCommand
{
    public static Task<RenameResult> ExecuteAsync(string connection, string routeId, string newName,
        CancellationToken ct)
    {
        DatabaseSession.RequireText(routeId, "Route ID");
        DatabaseSession.RequireText(newName, "New Name");

        return DatabaseSession.WriteAsync(connection, $"route:{routeId}", async db =>
        {
            var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == routeId, ct);
            if (route is null)
            {
                return RenameResult.NotFound;
            }

            route.Name = newName.Trim();
            return RenameResult.Renamed;
        }, ct);
    }
}
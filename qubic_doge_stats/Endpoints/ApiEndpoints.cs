using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Workers;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/snapshots/latest", (LiteDbContext db) =>
        {
            var snapshot = db.GetLatestSnapshot();
            return snapshot is not null ? Results.Ok(snapshot) : Results.NotFound();
        });

        api.MapGet("/snapshots/history", (LiteDbContext db, int limit = 100) =>
        {
            var snapshots = db.GetSnapshots(Math.Min(limit, 10080));
            return Results.Ok(snapshots);
        });

        api.MapGet("/pool/latest", () =>
        {
            var stats = PoolPollingWorker.LatestStats;
            return stats is not null ? Results.Ok(stats) : Results.NotFound();
        });

        api.MapGet("/pool/blocks", (LiteDbContext db) =>
        {
            var blocks = db.GetAllPoolBlocks();
            return Results.Ok(blocks);
        });

        api.MapGet("/network/stats", () =>
        {
            var stats = DogeExplorerPollingWorker.LatestStats;
            return stats is not null ? Results.Ok(stats) : Results.NotFound();
        });

    }
}

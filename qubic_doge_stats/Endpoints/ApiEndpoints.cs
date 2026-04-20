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

        api.MapGet("/pool/historical-reward", (LiteDbContext db) =>
        {
            const decimal dogePerBlock = 10_000m;
            var totalUsd = db.GetHistoricalBlockRewardUsd(dogePerBlock);
            var blocksWithPrice = db.GetAllPoolBlocks().Count(b => b.DogePriceUsdAtFind > 0);
            return Results.Ok(new { TotalUsd = totalUsd, BlocksWithPrice = blocksWithPrice });
        });

        api.MapGet("/network/stats", () =>
        {
            var stats = DogeExplorerPollingWorker.LatestStats;
            return stats is not null ? Results.Ok(stats) : Results.NotFound();
        });

        api.MapGet("/doge/price", () =>
        {
            var price = DogePricePollingWorker.LatestPrice;
            return price is not null ? Results.Ok(price) : Results.NotFound();
        });

        api.MapGet("/epochs/latest", (LiteDbContext db) =>
        {
            var summary = db.GetLatestEpochSummary();
            return summary is not null ? Results.Ok(summary) : Results.NotFound();
        });

        api.MapGet("/epochs/all", (LiteDbContext db) =>
        {
            return Results.Ok(db.GetAllEpochSummaries());
        });

        api.MapGet("/epochs/{epochNumber:int}", (int epochNumber, LiteDbContext db) =>
        {
            var summary = db.GetEpochSummary(epochNumber);
            return summary is not null ? Results.Ok(summary) : Results.NotFound();
        });

        api.MapGet("/stats/alltime", (LiteDbContext db) =>
        {
            var stats = db.GetAllTimeStats();
            return stats is not null ? Results.Ok(stats) : Results.NotFound();
        });

        api.MapGet("/mining-pools/ranking", () =>
        {
            var ranking = MiningPoolRankingWorker.LatestRanking;
            return ranking is not null ? Results.Ok(ranking) : Results.NotFound();
        });

        api.MapGet("/qu/price", () =>
        {
            var price = QuPricePollingWorker.LatestPrice;
            return price is not null ? Results.Ok(price) : Results.NotFound();
        });


}
}

using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Workers;
using qubic_doge_stats.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

        api.MapGet("/ltc/price", () =>
        {
            var price = LtcPricePollingWorker.LatestPrice;
            return price is not null ? Results.Ok(price) : Results.NotFound();
        });

        api.MapGet("/donations/top", (LiteDbContext db, int limit = 50) =>
            Results.Ok(db.GetTopDonors(Math.Min(limit, 100))));

        api.MapGet("/logs", (InMemoryLogBuffer logs, int limit = 200, string? level = null) =>
        {
            var entries = logs.GetAll();
            if (level is not null)
                entries = entries.Where(e => e.Level.Equals(level, StringComparison.OrdinalIgnoreCase)).ToList();
            return Results.Ok(entries.TakeLast(Math.Min(limit, 500)));
        });

        // ── Visitor Analytics ────────────────────────────────────────────────

        api.MapPost("/track", async (HttpContext ctx, LiteDbContext db, IHttpClientFactory httpFactory) =>
        {
            // Read path from body
            string path = "/";
            try
            {
                using var reader = new System.IO.StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(body);
                    if (doc.TryGetProperty("path", out var p))
                        path = p.GetString() ?? "/";
                }
            }
            catch { /* ignore malformed body */ }

            // Resolve real IP
            string? ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                         ?? ctx.Connection.RemoteIpAddress?.ToString();

            // Hash the IP — never store raw
            string ipHash = "";
            if (!string.IsNullOrEmpty(ip))
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
                ipHash = Convert.ToHexString(bytes).ToLowerInvariant();
            }

            // Geo-IP lookup — skip for localhost/private ranges, fail silently
            string? countryCode = null;
            string? countryName = null;
            if (!string.IsNullOrEmpty(ip) && !IsPrivateOrLocalIp(ip))
            {
                try
                {
                    using var geoClient = httpFactory.CreateClient();
                    geoClient.Timeout = TimeSpan.FromSeconds(3);
                    var geoJson = await geoClient.GetStringAsync($"http://ip-api.com/json/{ip}?fields=countryCode,country");
                    var geoDoc  = JsonSerializer.Deserialize<JsonElement>(geoJson);
                    if (geoDoc.TryGetProperty("countryCode", out var cc))
                        countryCode = cc.GetString();
                    if (geoDoc.TryGetProperty("country", out var cn))
                        countryName = cn.GetString();
                }
                catch { /* fail silently */ }
            }

            db.InsertVisitor(new VisitorEntry
            {
                Timestamp   = DateTime.UtcNow,
                IpHash      = ipHash,
                CountryCode = countryCode,
                CountryName = countryName,
                Path        = path,
            });

            return Results.Ok();
        });

        api.MapGet("/visitor-stats", (LiteDbContext db) =>
            Results.Ok(db.GetVisitorStats()));
    }

    private static bool IsPrivateOrLocalIp(string ip)
    {
        if (ip == "::1" || ip == "127.0.0.1") return true;
        if (!System.Net.IPAddress.TryParse(ip, out var addr)) return true;
        var bytes = addr.GetAddressBytes();
        if (bytes.Length != 4) return true;  // skip IPv6 for geo (ip-api.com supports it but keep simple)
        // 10.x.x.x
        if (bytes[0] == 10) return true;
        // 172.16.x.x – 172.31.x.x
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        // 192.168.x.x
        if (bytes[0] == 192 && bytes[1] == 168) return true;
        return false;
    }
}

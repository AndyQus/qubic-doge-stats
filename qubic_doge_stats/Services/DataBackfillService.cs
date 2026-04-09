using qubic_doge_stats.Infrastructure;

namespace qubic_doge_stats.Workers;

/// <summary>
/// Runs once at startup to fix pool_blocks that have a wrong or missing QubicEpoch.
/// Uses the deterministic Qubic epoch schedule (7 days, Wednesday 12:00 UTC) to assign
/// the correct epoch to every block based on its timestamp.
/// Safe to run on every startup — all operations are idempotent.
/// Enable/disable via DataBackfill:Enabled in appsettings.
/// </summary>
public class DataBackfillService : BackgroundService
{
    private const int MinMiningEpoch = 207;

    private readonly IServiceProvider _services;
    private readonly ILogger<DataBackfillService> _logger;
    private readonly bool _enabled;

    public DataBackfillService(IServiceProvider services, ILogger<DataBackfillService> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _enabled = config.GetValue("DataBackfill:Enabled", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("DataBackfill: disabled via config (DataBackfill:Enabled = false)");
            return;
        }

        // Wait for DogeStatsPollingWorker to fetch the current epoch from Qubic RPC
        while (Workers.DogeStatsPollingWorker.CurrentEpoch < MinMiningEpoch)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            if (stoppingToken.IsCancellationRequested) return;
        }

        var currentEpoch = Workers.DogeStatsPollingWorker.CurrentEpoch;
        _logger.LogInformation("DataBackfill: starting — current epoch {Epoch}", currentEpoch);

        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiteDbContext>();

            var allBlocks = db.GetAllPoolBlocks();
            var currentEpochStart = GetEpochStartUtc(DateTimeOffset.UtcNow);
            int fixedCount = 0;

            foreach (var block in allBlocks)
            {
                int weeksBack;
                if (block.Time >= currentEpochStart)
                    weeksBack = 0;
                else
                    weeksBack = (int)Math.Floor((currentEpochStart - block.Time).TotalDays / 7.0) + 1;

                var correctEpoch = currentEpoch - weeksBack;
                if (correctEpoch < MinMiningEpoch) continue;

                if (block.QubicEpoch != correctEpoch)
                {
                    _logger.LogInformation("DataBackfill: block {Height} ({Time:yyyy-MM-dd HH:mm}) epoch {Old} → {New}",
                        block.Height, block.Time, block.QubicEpoch, correctEpoch);
                    db.FixPoolBlockEpoch(block.Height, correctEpoch);
                    fixedCount++;
                }
            }

            _logger.LogInformation("DataBackfill: completed — {Fixed}/{Total} block(s) corrected", fixedCount, allBlocks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataBackfill: failed");
        }
    }

    private static DateTimeOffset GetEpochStartUtc(DateTimeOffset reference)
    {
        var refUtc = reference.UtcDateTime;
        int daysSinceWed = ((int)refUtc.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
        var lastWed = refUtc.Date.AddDays(-daysSinceWed);
        var candidate = new DateTimeOffset(lastWed, TimeSpan.Zero).AddHours(12);
        if (candidate > reference) candidate = candidate.AddDays(-7);
        return candidate;
    }
}

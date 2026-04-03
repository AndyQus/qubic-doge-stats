using LiteDB;
using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Services;

namespace qubic_doge_stats.Workers;

/// <summary>
/// Runs once at startup to backfill EpochSummary and AllTimeStats from historical snapshot data.
/// Also fixes pool_blocks and snapshots that have a wrong or missing QubicEpoch.
/// Safe to run on every startup — all operations are idempotent upserts.
/// </summary>
public class DataBackfillService : BackgroundService
{
    // Mining started in epoch 207 — ignore any snapshots from earlier epochs
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

        // Wait for DogeStats worker to run its first poll (which fetches the current epoch)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        _logger.LogInformation("DataBackfill: starting");

        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiteDbContext>();
            var epochSvc = scope.ServiceProvider.GetRequiredService<EpochSummaryService>();

            // Determine current epoch from the polling worker (fetched live from Qubic RPC)
            var currentEpoch = DogeStatsPollingWorker.CurrentEpoch;
            if (currentEpoch < MinMiningEpoch)
            {
                _logger.LogWarning("DataBackfill: CurrentEpoch={E} not yet known or below MinMiningEpoch={Min}, skipping",
                    currentEpoch, MinMiningEpoch);
                return;
            }

            // Step 1: Tag all untagged snapshots (QubicEpoch=0) using the Qubic epoch schedule.
            // This is the key fix: old snapshots saved before epoch-tracking was added are
            // assigned to the correct epoch based on their timestamp.
            FixSnapshotEpochs(db, currentEpoch);

            // Step 2: Collect all valid epochs present in snapshots (after tagging)
            var allEpochs = db.GetDistinctEpochsFromSnapshots()
                              .Where(e => e >= MinMiningEpoch)
                              .ToList();

            if (allEpochs.Count == 0)
            {
                _logger.LogInformation("DataBackfill: no snapshot data for epoch >= {Min} after tagging, skipping", MinMiningEpoch);
                return;
            }

            _logger.LogInformation("DataBackfill: processing epoch(s): {Epochs}", string.Join(", ", allEpochs));

            // Step 3: Fix pool_blocks with wrong/missing QubicEpoch
            FixPoolBlockEpochs(db, allEpochs);

            // Step 4: Process each epoch
            foreach (var epoch in allEpochs)
            {
                var snapshots = db.GetSnapshotsByEpoch(epoch);
                if (snapshots.Count == 0) continue;

                if (epoch == currentEpoch)
                {
                    // Running epoch: compute live summary (IsFinalized = false)
                    epochSvc.BackfillLiveEpoch(epoch, snapshots, db);
                    _logger.LogInformation("DataBackfill: current epoch {Epoch} live summary computed ({Count} snapshots)",
                        epoch, snapshots.Count);
                }
                else
                {
                    // Completed epoch: finalize (IsFinalized = true)
                    epochSvc.FinalizeEpoch(epoch);
                    _logger.LogInformation("DataBackfill: epoch {Epoch} finalized ({Count} snapshots)",
                        epoch, snapshots.Count);
                }
            }

            // Step 5: Rebuild AllTimeStats from FINALIZED epochs only.
            // The running epoch is added separately by the frontend to avoid double-counting.
            var finalizedSummaries = db.GetAllEpochSummaries()
                                       .Where(s => s.IsFinalized && s.EpochNumber >= MinMiningEpoch)
                                       .ToList();
            db.RebuildAllTimeStats(finalizedSummaries);

            _logger.LogInformation("DataBackfill: completed — {Total} epoch(s), {Fin} finalized",
                allEpochs.Count, finalizedSummaries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataBackfill: failed");
        }
    }

    /// <summary>
    /// Tags all snapshots with QubicEpoch=0 by mapping their timestamp to the Qubic epoch
    /// schedule (each epoch = exactly 7 days, starting Wednesday 12:00 UTC).
    /// </summary>
    private void FixSnapshotEpochs(LiteDbContext db, int currentEpoch)
    {
        var untagged = db.GetSnapshotsWithEpochZero();
        if (untagged.Count == 0) return;

        _logger.LogInformation("DataBackfill: tagging {Count} untagged snapshot(s) with QubicEpoch=0", untagged.Count);

        var currentEpochStart = GetEpochStartUtc(DateTimeOffset.UtcNow);
        var updates = new List<(ObjectId, int)>();

        foreach (var s in untagged)
        {
            // How many full weeks before the current epoch start?
            var weeksBack = (int)Math.Floor((currentEpochStart - s.Timestamp).TotalDays / 7.0);
            if (weeksBack < 0) weeksBack = 0; // snapshot is within current epoch window

            var assignedEpoch = currentEpoch - weeksBack;
            if (assignedEpoch >= MinMiningEpoch)
                updates.Add((s.Id, assignedEpoch));
        }

        if (updates.Count > 0)
        {
            var tagged = db.BulkSetSnapshotEpochs(updates);
            _logger.LogInformation("DataBackfill: tagged {Tagged}/{Total} snapshots with correct epoch", tagged, untagged.Count);
        }
    }

    /// <summary>
    /// Computes the start of the current Qubic epoch (Wednesday 12:00 UTC on or before reference).
    /// </summary>
    private static DateTimeOffset GetEpochStartUtc(DateTimeOffset reference)
    {
        var refUtc = reference.UtcDateTime;
        int daysSinceWed = ((int)refUtc.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
        var lastWed = refUtc.Date.AddDays(-daysSinceWed);
        var candidate = new DateTimeOffset(lastWed, TimeSpan.Zero).AddHours(12);
        if (candidate > reference) candidate = candidate.AddDays(-7);
        return candidate;
    }

    private void FixPoolBlockEpochs(LiteDbContext db, List<int> validEpochs)
    {
        var epochRanges = db.GetEpochTimeRanges();
        var wrongBlocks = db.GetPoolBlocksWithWrongEpoch(validEpochs);

        if (wrongBlocks.Count == 0)
        {
            _logger.LogInformation("DataBackfill: pool_blocks all have correct epoch");
            return;
        }

        _logger.LogInformation("DataBackfill: fixing {Count} pool_block(s) with wrong epoch", wrongBlocks.Count);

        foreach (var block in wrongBlocks)
        {
            // Find the epoch whose snapshot time range contains the block's timestamp
            var match = epochRanges
                .Where(kv => kv.Key >= MinMiningEpoch &&
                             block.Time >= kv.Value.Min.AddHours(-1) &&
                             block.Time <= kv.Value.Max.AddHours(1))
                .OrderBy(kv => Math.Abs((block.Time - kv.Value.Min).TotalMinutes +
                                        (block.Time - kv.Value.Max).TotalMinutes))
                .Select(kv => (int?)kv.Key)
                .FirstOrDefault();

            // Fallback: closest epoch by time distance
            if (match is null)
            {
                match = epochRanges
                    .Where(kv => kv.Key >= MinMiningEpoch)
                    .OrderBy(kv => Math.Min(
                        Math.Abs((block.Time - kv.Value.Min).TotalMinutes),
                        Math.Abs((block.Time - kv.Value.Max).TotalMinutes)))
                    .Select(kv => (int?)kv.Key)
                    .FirstOrDefault();
            }

            if (match is not null)
            {
                _logger.LogInformation("DataBackfill: block {Height}: epoch {Old} → {New}",
                    block.Height, block.QubicEpoch, match.Value);
                db.FixPoolBlockEpoch(block.Height, match.Value);
            }
        }
    }
}

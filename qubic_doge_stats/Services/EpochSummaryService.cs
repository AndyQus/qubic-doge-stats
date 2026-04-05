using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Services;

/// <summary>
/// Computes and persists EpochSummary + AllTimeStats.
/// UpdateLive() is called every poll to keep peaks and deltas current.
/// FinalizeEpoch() is called once at epoch end to set the final avg and mark as complete.
/// </summary>
public class EpochSummaryService
{
    private readonly LiteDbContext _db;
    private readonly ILogger<EpochSummaryService> _logger;

    public EpochSummaryService(LiteDbContext db, ILogger<EpochSummaryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Called on every poll. Updates peaks and counter deltas for the current epoch live.
    /// Also keeps AllTimeStats peaks up to date.
    /// </summary>
    public void UpdateLive(HashrateSnapshot snapshot)
    {
        if (snapshot.QubicEpoch <= 0) return;

        try
        {
            var summary = _db.GetEpochSummary(snapshot.QubicEpoch) ?? new EpochSummary
            {
                EpochNumber = snapshot.QubicEpoch,
                EpochStart = snapshot.Timestamp,
            };

            // Update peak hashrate
            if (snapshot.Hashrate > summary.PeakHashrate)
            {
                summary.PeakHashrate = snapshot.Hashrate;
                summary.PeakHashrateDisplay = FormatHashrate(snapshot.Hashrate);
                summary.PeakHashrateAt = snapshot.Timestamp;
            }

            // Update peak network share
            if (snapshot.NetworkHashrate > 0)
            {
                var sharePct = (double)snapshot.Hashrate / snapshot.NetworkHashrate * 100.0;
                if (sharePct > summary.PeakNetworkSharePct)
                {
                    summary.PeakNetworkSharePct = sharePct;
                    summary.PeakNetworkShareAt = snapshot.Timestamp;
                }
            }

            // Streaming SumPositiveIncrements:
            // On first snapshot set baseline = current value (ignore carry-over from previous epoch/session).
            // On each subsequent snapshot add only positive increments — pool restarts are handled
            // by treating a drop as a reset (delta ignored, baseline updated).
            var currentSharesValid = Workers.PoolPollingWorker.LatestStats?.SharesValid ?? 0;

            if (!summary.BaselineSet)
            {
                summary.BaselinePoolAccepted      = snapshot.PoolAccepted;
                summary.BaselinePoolRejected      = snapshot.PoolRejected;
                summary.BaselineSolutionsAccepted = snapshot.SolutionsAccepted;
                summary.BaselineSolutionsStale    = snapshot.SolutionsStale;
                summary.BaselineTasksDistributed  = snapshot.TasksDistributed;
                summary.BaselineSharesValid       = currentSharesValid;
                summary.BaselineSet = true;
                _logger.LogInformation("Epoch {Epoch} baseline set", snapshot.QubicEpoch);
            }
            else
            {
                if (snapshot.PoolAccepted      > summary.BaselinePoolAccepted)
                    summary.TotalPoolAccepted      += snapshot.PoolAccepted      - summary.BaselinePoolAccepted;
                if (snapshot.PoolRejected      > summary.BaselinePoolRejected)
                    summary.TotalPoolRejected      += snapshot.PoolRejected      - summary.BaselinePoolRejected;
                if (snapshot.SolutionsAccepted > summary.BaselineSolutionsAccepted)
                    summary.TotalSolutionsAccepted += snapshot.SolutionsAccepted - summary.BaselineSolutionsAccepted;
                if (snapshot.SolutionsStale    > summary.BaselineSolutionsStale)
                    summary.TotalSolutionsStale    += snapshot.SolutionsStale    - summary.BaselineSolutionsStale;
                if (snapshot.TasksDistributed  > summary.BaselineTasksDistributed)
                    summary.TotalTasksDistributed  += snapshot.TasksDistributed  - summary.BaselineTasksDistributed;
                if (currentSharesValid         > summary.BaselineSharesValid)
                    summary.SharesValid            += currentSharesValid         - summary.BaselineSharesValid;

                // Always update baseline to current value (tracks prev poll for next increment)
                summary.BaselinePoolAccepted      = snapshot.PoolAccepted;
                summary.BaselinePoolRejected      = snapshot.PoolRejected;
                summary.BaselineSolutionsAccepted = snapshot.SolutionsAccepted;
                summary.BaselineSolutionsStale    = snapshot.SolutionsStale;
                summary.BaselineTasksDistributed  = snapshot.TasksDistributed;
                summary.BaselineSharesValid       = currentSharesValid;
            }

            // Live block counts — never let them decrease due to race conditions at startup
            summary.BlocksFound     = Math.Max(summary.BlocksFound,     _db.GetBlocksFoundByEpoch(snapshot.QubicEpoch));
            summary.BlocksConfirmed = Math.Max(summary.BlocksConfirmed, _db.GetBlocksConfirmedByEpoch(snapshot.QubicEpoch));
            summary.EpochEnd = snapshot.Timestamp;

            _db.UpsertEpochSummary(summary);

            // Keep AllTimeStats peaks current (counters only finalized at epoch end)
            UpdateAllTimePeaks(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateLive failed for epoch {Epoch}", snapshot.QubicEpoch);
        }
    }

    /// <summary>
    /// Called by DataBackfillService for the current running epoch using all snapshots collected so far.
    /// Computes peaks and deltas from the full snapshot history — handles pool restarts correctly.
    /// </summary>
    public void BackfillLiveEpoch(int epochNumber, List<HashrateSnapshot> snapshots, LiteDbContext db)
    {
        try
        {
            var summary = db.GetEpochSummary(epochNumber) ?? new EpochSummary { EpochNumber = epochNumber };

            summary.EpochStart = snapshots.First().Timestamp;
            summary.EpochEnd   = snapshots.Last().Timestamp;

            // Peak hashrate
            var peakSnap = snapshots.MaxBy(s => s.Hashrate)!;
            summary.PeakHashrate        = peakSnap.Hashrate;
            summary.PeakHashrateDisplay = FormatHashrate(peakSnap.Hashrate);
            summary.PeakHashrateAt      = peakSnap.Timestamp;

            // Peak network share
            var withNet = snapshots.Where(s => s.NetworkHashrate > 0).ToList();
            if (withNet.Count > 0)
            {
                var peakShare = withNet.MaxBy(s => (double)s.Hashrate / s.NetworkHashrate)!;
                summary.PeakNetworkSharePct = (double)peakShare.Hashrate / peakShare.NetworkHashrate * 100.0;
                summary.PeakNetworkShareAt  = peakShare.Timestamp;
            }

            // Counter totals: sum of positive increments (handles pool restarts / resets)
            summary.TotalPoolAccepted      = SumPositiveIncrements(snapshots, s => s.PoolAccepted);
            summary.TotalPoolRejected      = SumPositiveIncrements(snapshots, s => s.PoolRejected);
            summary.TotalSolutionsAccepted = SumPositiveIncrements(snapshots, s => s.SolutionsAccepted);
            summary.TotalSolutionsStale    = SumPositiveIncrements(snapshots, s => s.SolutionsStale);
            summary.TotalTasksDistributed  = SumPositiveIncrements(snapshots, s => s.TasksDistributed);

            // Avg hashrate
            summary.AvgHashrate        = (long)snapshots.Average(s => s.Hashrate);
            summary.AvgHashrateDisplay = FormatHashrate(summary.AvgHashrate);

            // Blocks
            summary.BlocksFound     = db.GetBlocksFoundByEpoch(epochNumber);
            summary.BlocksConfirmed = db.GetBlocksConfirmedByEpoch(epochNumber);

            // Set baseline to LAST snapshot so UpdateLive continues streaming from the correct point
            summary.BaselineSet = true;
            var last = snapshots.Last();
            summary.BaselinePoolAccepted      = last.PoolAccepted;
            summary.BaselinePoolRejected      = last.PoolRejected;
            summary.BaselineSolutionsAccepted = last.SolutionsAccepted;
            summary.BaselineSolutionsStale    = last.SolutionsStale;
            summary.BaselineTasksDistributed  = last.TasksDistributed;

            db.UpsertEpochSummary(summary);
            _logger.LogInformation(
                "DataBackfill: epoch {Epoch} live summary — peak {Peak}, blocks {Blocks}, accepted {Acc}",
                epochNumber, summary.PeakHashrateDisplay, summary.BlocksFound, summary.TotalPoolAccepted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackfillLiveEpoch failed for epoch {Epoch}", epochNumber);
        }
    }

    private static int SumPositiveIncrements(List<HashrateSnapshot> snapshots, Func<HashrateSnapshot, int> selector)
    {
        var sum = 0;
        var prev = -1;
        foreach (var s in snapshots)
        {
            var val = selector(s);
            if (prev >= 0 && val > prev) sum += val - prev;
            prev = val;
        }
        return sum;
    }

    /// <summary>
    /// Called once when the epoch ends. Computes final AVG hashrate and marks the epoch as finalized.
    /// Also adds the epoch's counter totals to AllTimeStats.
    /// </summary>
    public void FinalizeEpoch(int epochNumber)
    {
        try
        {
            var snapshots = _db.GetSnapshotsByEpoch(epochNumber);
            if (snapshots.Count == 0)
            {
                _logger.LogWarning("No snapshots for epoch {Epoch} — skipping finalize", epochNumber);
                return;
            }

            var summary = _db.GetEpochSummary(epochNumber) ?? new EpochSummary { EpochNumber = epochNumber };

            var avgHashrate = (long)snapshots.Average(s => s.Hashrate);
            summary.AvgHashrate = avgHashrate;
            summary.AvgHashrateDisplay = FormatHashrate(avgHashrate);
            summary.EpochStart = snapshots.First().Timestamp;
            summary.EpochEnd = snapshots.Last().Timestamp;
            summary.IsFinalized = true;

            _db.UpsertEpochSummary(summary);
            _logger.LogInformation("Epoch {Epoch} finalized: avg {Avg}, peak {Peak}, blocks {Blocks}",
                epochNumber, summary.AvgHashrateDisplay, summary.PeakHashrateDisplay, summary.BlocksFound);

            AddEpochCountersToAllTime(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FinalizeEpoch failed for epoch {Epoch}", epochNumber);
        }
    }

    private void UpdateAllTimePeaks(EpochSummary epoch)
    {
        var existing = _db.GetAllTimeStats() ?? new AllTimeStats();
        var changed = false;

        if (epoch.PeakHashrate > existing.PeakHashrate)
        {
            existing.PeakHashrate = epoch.PeakHashrate;
            existing.PeakHashrateDisplay = epoch.PeakHashrateDisplay;
            existing.PeakHashrateEpoch = epoch.EpochNumber;
            existing.PeakHashrateAt = epoch.PeakHashrateAt;
            changed = true;
        }

        if (epoch.PeakNetworkSharePct > existing.PeakNetworkSharePct)
        {
            existing.PeakNetworkSharePct = epoch.PeakNetworkSharePct;
            existing.PeakNetworkShareEpoch = epoch.EpochNumber;
            existing.PeakNetworkShareAt = epoch.PeakNetworkShareAt;
            changed = true;
        }

        // Counter peaks — highest single-epoch value ever recorded
        if (epoch.TotalPoolAccepted > existing.PeakPoolAccepted)
        { existing.PeakPoolAccepted = epoch.TotalPoolAccepted; existing.PeakPoolAcceptedEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.TotalPoolRejected > existing.PeakPoolRejected)
        { existing.PeakPoolRejected = epoch.TotalPoolRejected; existing.PeakPoolRejectedEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.TotalSolutionsAccepted > existing.PeakSolutionsAccepted)
        { existing.PeakSolutionsAccepted = epoch.TotalSolutionsAccepted; existing.PeakSolutionsAcceptedEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.TotalSolutionsStale > existing.PeakSolutionsStale)
        { existing.PeakSolutionsStale = epoch.TotalSolutionsStale; existing.PeakSolutionsStaleEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.TotalTasksDistributed > existing.PeakTasksDistributed)
        { existing.PeakTasksDistributed = epoch.TotalTasksDistributed; existing.PeakTasksDistributedEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.BlocksFound > existing.PeakBlocksFound)
        { existing.PeakBlocksFound = epoch.BlocksFound; existing.PeakBlocksFoundEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.BlocksConfirmed > existing.PeakBlocksConfirmed)
        { existing.PeakBlocksConfirmed = epoch.BlocksConfirmed; existing.PeakBlocksConfirmedEpoch = epoch.EpochNumber; changed = true; }
        if (epoch.SharesValid > existing.PeakSharesValid)
        { existing.PeakSharesValid = epoch.SharesValid; existing.PeakSharesValidEpoch = epoch.EpochNumber; changed = true; }

        if (changed)
        {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            _db.UpsertAllTimeStats(existing);
        }
    }

    private void AddEpochCountersToAllTime(EpochSummary epoch)
    {
        var existing = _db.GetAllTimeStats() ?? new AllTimeStats();

        existing.TotalPoolAccepted += epoch.TotalPoolAccepted;
        existing.TotalPoolRejected += epoch.TotalPoolRejected;
        existing.TotalSolutionsAccepted += epoch.TotalSolutionsAccepted;
        existing.TotalSolutionsStale += epoch.TotalSolutionsStale;
        existing.TotalTasksDistributed += epoch.TotalTasksDistributed;
        existing.TotalBlocksFound += epoch.BlocksFound;
        existing.TotalBlocksConfirmed += epoch.BlocksConfirmed;
        existing.TotalSharesValid += epoch.SharesValid;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        _db.UpsertAllTimeStats(existing);
        _logger.LogInformation("AllTimeStats counters updated after epoch {Epoch} finalized", epoch.EpochNumber);
    }

    internal static string FormatHashrate(long hashrate)
    {
        if (hashrate >= 1_000_000_000_000L) return $"{hashrate / 1_000_000_000_000.0:F2} TH/s";
        if (hashrate >= 1_000_000_000L)     return $"{hashrate / 1_000_000_000.0:F2} GH/s";
        if (hashrate >= 1_000_000L)         return $"{hashrate / 1_000_000.0:F2} MH/s";
        return $"{hashrate} H/s";
    }
}

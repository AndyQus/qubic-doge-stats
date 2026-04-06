using LiteDB;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Infrastructure;

public class LiteDbContext : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly object _lock = new();

    public LiteDbContext(IConfiguration configuration)
    {
        var filename = configuration["LiteDb:Filename"] ?? "Data/doge_stats.db";
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
        {
            var dbFile = Environment.GetEnvironmentVariable("LITEDB_FILE") ?? "doge_stats.db";
            filename = Path.Combine(dataDir, dbFile);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
        _db = new LiteDatabase(filename);
        EnsureIndexes();
        // One-time cleanup: remove any fake blocks left from development phase
        _db.GetCollection("pool_blocks").DeleteMany("$.IsFake = true");
    }

    private void EnsureIndexes()
    {
        var col = _db.GetCollection<HashrateSnapshot>("snapshots");
        col.EnsureIndex(x => x.Timestamp);

        var poolCol = _db.GetCollection<PoolBlock>("pool_blocks");
        poolCol.EnsureIndex(x => x.Height);
        poolCol.EnsureIndex(x => x.QubicEpoch);

        var epochCol = _db.GetCollection<EpochSummary>("epoch_summaries");
        epochCol.EnsureIndex(x => x.EpochNumber, unique: true);
    }

    public List<HashrateSnapshot> GetSnapshots(int limit = 1440)
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .Query()
                .OrderByDescending(x => x.Timestamp)
                .Limit(limit)
                .ToList();
        }
    }

    public HashrateSnapshot? GetLatestSnapshot()
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .Query()
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefault();
        }
    }

    public void InsertSnapshot(HashrateSnapshot snapshot)
    {
        lock (_lock)
        {
            _db.GetCollection<HashrateSnapshot>("snapshots").Insert(snapshot);
        }
    }

    public void UpsertPoolBlock(PoolBlock block)
    {
        lock (_lock)
        {
            var col = _db.GetCollection<PoolBlock>("pool_blocks");
            // Deduplicate by Height; prefer a record with a known hash over one without
            var existing = col.FindOne(x => x.Height == block.Height);
            if (existing is null)
            {
                col.Insert(block);
            }
            else
            {
                block.Id = existing.Id;
                // Keep existing hash/worker if the new record has none (lastBlock fallback)
                if (string.IsNullOrEmpty(block.Hash) && !string.IsNullOrEmpty(existing.Hash))
                {
                    block.Hash = existing.Hash;
                    block.Worker = existing.Worker;
                }
                // Never overwrite a recorded price with zero
                if (block.DogePriceUsdAtFind == 0 && existing.DogePriceUsdAtFind > 0)
                    block.DogePriceUsdAtFind = existing.DogePriceUsdAtFind;
                col.Update(block);
            }
        }
    }

    public decimal GetHistoricalBlockRewardUsd(decimal dogePerBlock)
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .FindAll()
                .Where(b => b.DogePriceUsdAtFind > 0)
                .Sum(b => b.DogePriceUsdAtFind * dogePerBlock);
        }
    }

    public List<PoolBlock> GetAllPoolBlocks()
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .Query()
                .OrderByDescending(x => x.Time)
                .ToList();
        }
    }

    // ── Epoch Summaries ──────────────────────────────────────────────────────

    public void UpsertEpochSummary(EpochSummary summary)
    {
        lock (_lock)
        {
            var col = _db.GetCollection<EpochSummary>("epoch_summaries");
            var existing = col.FindOne(x => x.EpochNumber == summary.EpochNumber);
            if (existing is not null)
                summary.Id = existing.Id;
            col.Upsert(summary);
        }
    }

    public EpochSummary? GetEpochSummary(int epochNumber)
    {
        lock (_lock)
        {
            return _db.GetCollection<EpochSummary>("epoch_summaries")
                .FindOne(x => x.EpochNumber == epochNumber);
        }
    }

    public List<EpochSummary> GetAllEpochSummaries()
    {
        lock (_lock)
        {
            return _db.GetCollection<EpochSummary>("epoch_summaries")
                .Query()
                .OrderByDescending(x => x.EpochNumber)
                .ToList();
        }
    }

    public EpochSummary? GetLatestEpochSummary()
    {
        lock (_lock)
        {
            return _db.GetCollection<EpochSummary>("epoch_summaries")
                .Query()
                .OrderByDescending(x => x.EpochNumber)
                .FirstOrDefault();
        }
    }

    // ── All-Time Stats ───────────────────────────────────────────────────────

    public void UpsertAllTimeStats(AllTimeStats stats)
    {
        lock (_lock)
        {
            _db.GetCollection<AllTimeStats>("alltime_stats").Upsert(stats);
        }
    }

    public AllTimeStats? GetAllTimeStats()
    {
        lock (_lock)
        {
            return _db.GetCollection<AllTimeStats>("alltime_stats").FindById(1);
        }
    }

    // ── Snapshot helpers for epoch aggregation ───────────────────────────────

    public List<int> GetDistinctEpochsFromSnapshots()
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .FindAll()
                .Where(s => s.QubicEpoch > 0)
                .Select(s => s.QubicEpoch)
                .Distinct()
                .OrderBy(e => e)
                .ToList();
        }
    }

    public Dictionary<int, (DateTimeOffset Min, DateTimeOffset Max)> GetEpochTimeRanges()
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .FindAll()
                .Where(s => s.QubicEpoch > 0)
                .GroupBy(s => s.QubicEpoch)
                .ToDictionary(
                    g => g.Key,
                    g => (g.Min(s => s.Timestamp), g.Max(s => s.Timestamp)));
        }
    }

    public void FixPoolBlockEpoch(long height, int correctEpoch)
    {
        lock (_lock)
        {
            var col = _db.GetCollection<PoolBlock>("pool_blocks");
            var block = col.FindOne(x => x.Height == height);
            if (block is not null && block.QubicEpoch != correctEpoch)
            {
                block.QubicEpoch = correctEpoch;
                col.Update(block);
            }
        }
    }

    public List<PoolBlock> GetPoolBlocksWithWrongEpoch(IEnumerable<int> validEpochs)
    {
        var validSet = validEpochs.ToHashSet();
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .FindAll()
                .Where(b => !validSet.Contains(b.QubicEpoch))
                .ToList();
        }
    }

    public List<HashrateSnapshot> GetSnapshotsWithEpochZero()
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .Query()
                .Where(x => x.QubicEpoch == 0)
                .OrderBy(x => x.Timestamp)
                .ToList();
        }
    }

    public int BulkSetSnapshotEpochs(List<(ObjectId Id, int Epoch)> updates)
    {
        lock (_lock)
        {
            var col = _db.GetCollection<HashrateSnapshot>("snapshots");
            int count = 0;
            foreach (var (id, epoch) in updates)
            {
                var s = col.FindById(id);
                if (s is null) continue;
                s.QubicEpoch = epoch;
                col.Update(s);
                count++;
            }
            return count;
        }
    }

    public List<HashrateSnapshot> GetSnapshotsByEpoch(int epochNumber)
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .Query()
                .Where(x => x.QubicEpoch == epochNumber)
                .OrderBy(x => x.Timestamp)
                .ToList();
        }
    }

    public int GetBlocksFoundByEpoch(int epochNumber)
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .Count(x => x.QubicEpoch == epochNumber);
        }
    }

    public int GetBlocksConfirmedByEpoch(int epochNumber)
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .Count(x => x.QubicEpoch == epochNumber && x.Confirmed);
        }
    }

    public void RebuildAllTimeStats(List<EpochSummary> summaries)
    {
        // Always start fresh — clears stale data even when no epochs are finalized yet
        var stats = new AllTimeStats { UpdatedAt = DateTimeOffset.UtcNow };
        if (summaries.Count == 0)
        {
            UpsertAllTimeStats(stats);
            return;
        }
        foreach (var s in summaries)
        {
            stats.TotalPoolAccepted      += s.TotalPoolAccepted;
            stats.TotalPoolRejected      += s.TotalPoolRejected;
            stats.TotalSolutionsAccepted += s.TotalSolutionsAccepted;
            stats.TotalSolutionsStale    += s.TotalSolutionsStale;
            stats.TotalTasksDistributed  += s.TotalTasksDistributed;
            stats.TotalBlocksFound       += s.BlocksFound;
            stats.TotalBlocksConfirmed   += s.BlocksConfirmed;
            stats.TotalSharesValid       += s.SharesValid;

            if (s.PeakHashrate > stats.PeakHashrate)
            {
                stats.PeakHashrate        = s.PeakHashrate;
                stats.PeakHashrateDisplay = s.PeakHashrateDisplay;
                stats.PeakHashrateEpoch   = s.EpochNumber;
                stats.PeakHashrateAt      = s.PeakHashrateAt;
            }
            if (s.PeakNetworkSharePct > stats.PeakNetworkSharePct)
            {
                stats.PeakNetworkSharePct   = s.PeakNetworkSharePct;
                stats.PeakNetworkShareEpoch = s.EpochNumber;
                stats.PeakNetworkShareAt    = s.PeakNetworkShareAt;
            }

            // Counter peaks per epoch
            if (s.TotalPoolAccepted > stats.PeakPoolAccepted)
            { stats.PeakPoolAccepted = s.TotalPoolAccepted; stats.PeakPoolAcceptedEpoch = s.EpochNumber; }
            if (s.TotalPoolRejected > stats.PeakPoolRejected)
            { stats.PeakPoolRejected = s.TotalPoolRejected; stats.PeakPoolRejectedEpoch = s.EpochNumber; }
            if (s.TotalSolutionsAccepted > stats.PeakSolutionsAccepted)
            { stats.PeakSolutionsAccepted = s.TotalSolutionsAccepted; stats.PeakSolutionsAcceptedEpoch = s.EpochNumber; }
            if (s.TotalSolutionsStale > stats.PeakSolutionsStale)
            { stats.PeakSolutionsStale = s.TotalSolutionsStale; stats.PeakSolutionsStaleEpoch = s.EpochNumber; }
            if (s.TotalTasksDistributed > stats.PeakTasksDistributed)
            { stats.PeakTasksDistributed = s.TotalTasksDistributed; stats.PeakTasksDistributedEpoch = s.EpochNumber; }
            if (s.BlocksFound > stats.PeakBlocksFound)
            { stats.PeakBlocksFound = s.BlocksFound; stats.PeakBlocksFoundEpoch = s.EpochNumber; }
            if (s.BlocksConfirmed > stats.PeakBlocksConfirmed)
            { stats.PeakBlocksConfirmed = s.BlocksConfirmed; stats.PeakBlocksConfirmedEpoch = s.EpochNumber; }
            if (s.SharesValid > stats.PeakSharesValid)
            { stats.PeakSharesValid = s.SharesValid; stats.PeakSharesValidEpoch = s.EpochNumber; }
        }
        UpsertAllTimeStats(stats);
    }

    /// <summary>
    /// Called after each ranking poll. Updates BestRank in the current EpochSummary and AllTimeStats
    /// if the new rank is better (lower). Rank 0 means "not yet recorded".
    /// </summary>
    public void UpdateRankingBests(int epochNumber, int rank, DateTimeOffset fetchedAt)
    {
        if (rank <= 0 || epochNumber <= 0) return;
        lock (_lock)
        {
            // Epoch summary
            var epochCol = _db.GetCollection<EpochSummary>("epoch_summaries");
            var epoch = epochCol.FindOne(x => x.EpochNumber == epochNumber);
            if (epoch is not null && (epoch.BestRank == 0 || rank < epoch.BestRank))
            {
                epoch.BestRank   = rank;
                epoch.BestRankAt = fetchedAt;
                epochCol.Update(epoch);
            }

            // All-time stats
            var atsCol = _db.GetCollection<AllTimeStats>("alltime_stats");
            var ats = atsCol.FindById(1) ?? new AllTimeStats { UpdatedAt = fetchedAt };
            if (ats.BestRank == 0 || rank < ats.BestRank)
            {
                ats.BestRank      = rank;
                ats.BestRankEpoch = epochNumber;
                ats.BestRankAt    = fetchedAt;
                ats.UpdatedAt     = fetchedAt;
                atsCol.Upsert(ats);
            }
        }
    }

    public void Dispose() => _db.Dispose();
}

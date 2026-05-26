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
        RepairEpoch213IfNeeded();
    }

    private void RepairEpoch213IfNeeded()
    {
        var col     = _db.GetCollection<PoolBlock>("pool_blocks");
        var backup  = _db.GetCollection<PoolBlock>("repair_backup_e213");

        // Use FindAll + LINQ-to-Objects to avoid LiteDB long/int comparison issues
        var allE213 = col.FindAll().Where(b => b.QubicEpoch == 213).ToList();

        // Step 1: Find DOGE blocks with LTC height range (< 4_000_000) in E213
        var wrongDogeAsLtc = allE213.Where(b => b.Chain == "DOGE" && b.Height < 4_000_000).ToList();

        // Step 2: Find duplicate DOGE blocks in E213 (same Height, keep oldest)
        var e213Doge = allE213.Where(b => b.Chain == "DOGE").OrderBy(b => b.Time).ToList();
        var seen     = new HashSet<long>();
        var dogeDuplicates = new List<PoolBlock>();
        foreach (var block in e213Doge)
        {
            if (!seen.Add(block.Height))
                dogeDuplicates.Add(block);
        }

        // Nothing to do — all clean
        if (wrongDogeAsLtc.Count == 0 && dogeDuplicates.Count == 0)
            return;

        // Back up all E213 blocks before touching anything (only if not already backed up)
        if (!backup.Exists(x => x.QubicEpoch == 213))
        {
            foreach (var b in allE213)
                backup.Insert(new PoolBlock
                {
                    Chain              = b.Chain,
                    Height             = b.Height,
                    Hash               = b.Hash,
                    Worker             = b.Worker,
                    Time               = b.Time,
                    Confirmed          = b.Confirmed,
                    QubicEpoch         = b.QubicEpoch,
                    DogePriceUsdAtFind = b.DogePriceUsdAtFind,
                    LtcPriceUsdAtFind  = b.LtcPriceUsdAtFind,
                });
        }

        // Fix Step 1: misclassified LTC blocks stored as DOGE
        foreach (var wrong in wrongDogeAsLtc)
        {
            var existingLtc = col.FindOne(x => x.Chain == "LTC" && x.Height == wrong.Height);
            if (existingLtc is not null)
            {
                // Correct LTC entry already exists — move wrong DOGE copy to backup and delete
                backup.Insert(new PoolBlock
                {
                    Chain              = wrong.Chain,
                    Height             = wrong.Height,
                    Hash               = wrong.Hash,
                    Worker             = wrong.Worker,
                    Time               = wrong.Time,
                    Confirmed          = wrong.Confirmed,
                    QubicEpoch         = wrong.QubicEpoch,
                    DogePriceUsdAtFind = wrong.DogePriceUsdAtFind,
                    LtcPriceUsdAtFind  = wrong.LtcPriceUsdAtFind,
                });
                col.Delete(wrong.Id);
            }
            else
            {
                // No LTC entry yet — convert in place to LTC
                wrong.Chain = "LTC";
                if (wrong.LtcPriceUsdAtFind == 0 && wrong.DogePriceUsdAtFind > 0)
                    wrong.LtcPriceUsdAtFind = wrong.DogePriceUsdAtFind;
                wrong.DogePriceUsdAtFind = 0m;
                col.Update(wrong);
            }
        }

        // Fix Step 2: remove DOGE duplicates — move to backup and delete
        foreach (var dupe in dogeDuplicates)
        {
            backup.Insert(new PoolBlock
            {
                Chain              = dupe.Chain,
                Height             = dupe.Height,
                Hash               = dupe.Hash,
                Worker             = dupe.Worker,
                Time               = dupe.Time,
                Confirmed          = dupe.Confirmed,
                QubicEpoch         = dupe.QubicEpoch,
                DogePriceUsdAtFind = dupe.DogePriceUsdAtFind,
                LtcPriceUsdAtFind  = dupe.LtcPriceUsdAtFind,
            });
            col.Delete(dupe.Id);
        }

        // Rebuild EpochSummary for E213 from corrected pool_blocks
        var summaryCol = _db.GetCollection<EpochSummary>("epoch_summaries");
        var summary213 = summaryCol.FindOne(x => x.EpochNumber == 213);
        if (summary213 is not null)
        {
            var blocks213 = col.FindAll().Where(b => b.QubicEpoch == 213).ToList();
            summary213.BlocksFound        = blocks213.Count(b => b.Chain == "DOGE");
            summary213.BlocksConfirmed    = blocks213.Count(b => b.Chain == "DOGE" && b.Confirmed);
            summary213.LtcBlocksFound     = blocks213.Count(b => b.Chain == "LTC");
            summary213.LtcBlocksConfirmed = blocks213.Count(b => b.Chain == "LTC" && b.Confirmed);
            summaryCol.Update(summary213);

            // Rebuild AllTimeStats from all corrected epoch summaries
            RebuildAllTimeStats(summaryCol.FindAll().ToList());
        }
    }

    private void EnsureIndexes()
    {
        var col = _db.GetCollection<HashrateSnapshot>("snapshots");
        col.EnsureIndex(x => x.Timestamp);

        var poolCol = _db.GetCollection<PoolBlock>("pool_blocks");
        poolCol.EnsureIndex(x => x.Height);
        poolCol.EnsureIndex(x => x.Chain);
        poolCol.EnsureIndex(x => x.QubicEpoch);

        var epochCol = _db.GetCollection<EpochSummary>("epoch_summaries");
        epochCol.EnsureIndex(x => x.EpochNumber, unique: true);

        var donCol = _db.GetCollection<DonationEntry>("donations");
        donCol.EnsureIndex(x => x.Address);

        var visitorCol = _db.GetCollection<VisitorEntry>("visitors");
        visitorCol.EnsureIndex(x => x.Timestamp);
        visitorCol.EnsureIndex(x => x.IpHash);
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
            // Deduplicate by (Chain, Height) — height alone is not unique across chains
            var existing = col.FindOne(x => x.Chain == block.Chain && x.Height == block.Height);
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
                if (block.LtcPriceUsdAtFind == 0 && existing.LtcPriceUsdAtFind > 0)
                    block.LtcPriceUsdAtFind = existing.LtcPriceUsdAtFind;
                // Never overwrite a valid epoch — once correctly set, epoch is immutable
                if (existing.QubicEpoch > 0)
                    block.QubicEpoch = existing.QubicEpoch;
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

    public void FixPoolBlockEpoch(long height, int correctEpoch, string chain = "DOGE")
    {
        lock (_lock)
        {
            var col = _db.GetCollection<PoolBlock>("pool_blocks");
            var block = col.FindOne(x => x.Chain == chain && x.Height == height);
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

    public int GetBlocksFoundByEpoch(int epochNumber, string chain = "DOGE")
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .Count(x => x.QubicEpoch == epochNumber && x.Chain == chain);
        }
    }

    public int GetBlocksConfirmedByEpoch(int epochNumber, string chain = "DOGE")
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .Count(x => x.QubicEpoch == epochNumber && x.Chain == chain && x.Confirmed);
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
            if (s.LtcBlocksFound > stats.PeakLtcBlocksFound)
            { stats.PeakLtcBlocksFound = s.LtcBlocksFound; stats.PeakLtcBlocksFoundEpoch = s.EpochNumber; }

            stats.TotalLtcBlocksFound     += s.LtcBlocksFound;
            stats.TotalLtcBlocksConfirmed += s.LtcBlocksConfirmed;
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

    public List<TopDonor> GetTopDonors(int limit = 20)
    {
        lock (_lock)
        {
            return _db.GetCollection<DonationEntry>("donations")
                .Query()
                .ToList()
                .GroupBy(d => d.Address)
                .Select(g => new TopDonor
                {
                    Address = g.Key,
                    TotalQu = g.Sum(d => d.AmountQu),
                    Date = g.Max(d => d.Date).ToString("yyyy-MM-dd")
                })
                .OrderByDescending(d => d.TotalQu)
                .Take(limit)
                .ToList();
        }
    }

    public long GetLastDonationTick()
    {
        lock (_lock)
        {
            var col = _db.GetCollection("donation_settings");
            var doc = col.FindOne(x => x["_id"] == "last_tick");
            return doc?["value"].AsInt64 ?? 0;
        }
    }

    public void SetLastDonationTick(long tick)
    {
        lock (_lock)
        {
            var col = _db.GetCollection("donation_settings");
            var doc = new BsonDocument { ["_id"] = "last_tick", ["value"] = tick };
            col.Upsert(doc);
        }
    }

    public void InsertDonationIfNew(DonationEntry entry)
    {
        lock (_lock)
        {
            var col = _db.GetCollection<DonationEntry>("donations");
            var exists = col.Exists(x => x.Address == entry.Address && x.AmountQu == entry.AmountQu && x.Date == entry.Date);
            if (!exists)
                col.Insert(entry);
        }
    }

    // ── Visitor Analytics ────────────────────────────────────────────────────

    public void InsertVisitor(VisitorEntry entry)
    {
        lock (_lock)
        {
            _db.GetCollection<VisitorEntry>("visitors").Insert(entry);
        }
    }

    public VisitorStatsDto GetVisitorStats()
    {
        lock (_lock)
        {
            var col = _db.GetCollection<VisitorEntry>("visitors");
            var all = col.FindAll().ToList();

            var nowUtc     = DateTime.UtcNow;
            var today      = nowUtc.Date;
            var weekAgo    = nowUtc.AddDays(-7);
            var monthAgo   = nowUtc.AddDays(-30);
            var fiveMinAgo = nowUtc.AddHours(-1);

            var todayEntries    = all.Where(e => e.Timestamp >= today).ToList();
            var weekEntries     = all.Where(e => e.Timestamp >= weekAgo).ToList();
            var monthEntries    = all.Where(e => e.Timestamp >= monthAgo).ToList();
            var activeEntries   = all.Where(e => e.Timestamp >= fiveMinAgo).ToList();

            // Last 30 days grouped by date
            var last30Days = all
                .Where(e => e.Timestamp >= monthAgo)
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyVisitorCount
                {
                    Date          = g.Key.ToString("yyyy-MM-dd"),
                    PageViews     = g.Count(),
                    UniqueVisitors = g.Select(e => e.IpHash).Distinct().Count()
                })
                .ToList();

            // Last 24 months grouped by year-month
            var twoYearsAgo = nowUtc.AddMonths(-24);
            var last24Months = all
                .Where(e => e.Timestamp >= twoYearsAgo)
                .GroupBy(e => new { e.Timestamp.Year, e.Timestamp.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new MonthlyVisitorCount
                {
                    Month          = $"{g.Key.Year:0000}-{g.Key.Month:00}",
                    PageViews      = g.Count(),
                    UniqueVisitors = g.Select(e => e.IpHash).Distinct().Count()
                })
                .ToList();

            // All years grouped
            var allYears = all
                .GroupBy(e => e.Timestamp.Year)
                .OrderBy(g => g.Key)
                .Select(g => new YearlyVisitorCount
                {
                    Year           = g.Key,
                    PageViews      = g.Count(),
                    UniqueVisitors = g.Select(e => e.IpHash).Distinct().Count()
                })
                .ToList();

            static List<CountryCount> TopCountries(IEnumerable<VisitorEntry> entries) =>
                entries
                    .Where(e => !string.IsNullOrEmpty(e.CountryCode))
                    .GroupBy(e => new { e.CountryCode, e.CountryName })
                    .Select(g => new CountryCount
                    {
                        CountryCode = g.Key.CountryCode ?? "",
                        CountryName = g.Key.CountryName ?? "",
                        Count       = g.Count()
                    })
                    .OrderByDescending(c => c.Count)
                    .Take(10)
                    .ToList();

            return new VisitorStatsDto
            {
                TotalPageViews           = all.Count,
                UniqueVisitorsToday      = todayEntries.Select(e => e.IpHash).Distinct().Count(),
                UniqueVisitorsThisWeek   = weekEntries.Select(e => e.IpHash).Distinct().Count(),
                UniqueVisitorsThisMonth  = monthEntries.Select(e => e.IpHash).Distinct().Count(),
                ActiveVisitorsLastHour   = activeEntries.Select(e => e.IpHash).Distinct().Count(),
                Last30Days               = last30Days,
                Last24Months             = last24Months,
                AllYears                 = allYears,
                TopCountries30Days       = TopCountries(monthEntries),
                TopCountriesAllTime      = TopCountries(all),
            };
        }
    }

    public void Dispose() => _db.Dispose();
}

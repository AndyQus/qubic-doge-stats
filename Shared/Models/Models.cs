using LiteDB;

namespace qubic_doge_stats.Shared.Models;

public class HashrateSnapshot
{
    [BsonId(autoId: true)]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public DateTimeOffset Timestamp { get; set; }
    public long Hashrate { get; set; }
    public string HashrateDisplay { get; set; } = "";
    public long PoolDifficulty { get; set; }
    public int TasksDistributed { get; set; }
    public int ActiveTasks { get; set; }
    public int ConnectedPeers { get; set; }
    public int TotalPeers { get; set; }
    public int PoolAccepted { get; set; }
    public int PoolRejected { get; set; }
    public int PoolSubmitted { get; set; }
    public int SolutionsAccepted { get; set; }
    public int SolutionsReceived { get; set; }
    public int SolutionsRejected { get; set; }
    public int SolutionsStale { get; set; }
    public long UptimeSeconds { get; set; }
    public int QueueSolutions { get; set; }
    public int QueueStratum { get; set; }
    public int QubicEpoch { get; set; }
    public long NetworkHashrate { get; set; }  // H/s (24h avg) at snapshot time — for network share calculation
}

// API response models matching https://doge-stats.qubic.org/dispatcher.json
public class DispatcherResponse
{
    public int ActiveTasks { get; set; }
    public MiningStats Mining { get; set; } = new();
    public NetworkStats Network { get; set; } = new();
    public PoolStats Pool { get; set; } = new();
    public QueueStats Queues { get; set; } = new();
    public SolutionStats Solutions { get; set; } = new();
    public long Timestamp { get; set; }
    public long UptimeSeconds { get; set; }
}

public class MiningStats
{
    public long Hashrate { get; set; }
    public string HashrateDisplay { get; set; } = "";
    public long PoolDifficulty { get; set; }
    public int TasksDistributed { get; set; }
}

public class NetworkStats
{
    public int ConnectedPeers { get; set; }
    public int TotalPeers { get; set; }
}

public class PoolStats
{
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Submitted { get; set; }
}

public class QueueStats
{
    public int Solutions { get; set; }
    public int Stratum { get; set; }
}

public class SolutionStats
{
    public int Accepted { get; set; }
    public int Received { get; set; }
    public int Rejected { get; set; }
    public int Stale { get; set; }
}

// Qubic RPC response models
public class QubicTickInfoResponse
{
    public TickInfo TickInfo { get; set; } = new();
}

public class TickInfo
{
    public long Tick { get; set; }
    public int Duration { get; set; }
    public int Epoch { get; set; }
    public long InitialTick { get; set; }
}

// PoolBlock — one confirmed or pending DOGE or LTC block find, stored permanently
public class PoolBlock
{
    [BsonId(autoId: true)]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public string Chain { get; set; } = "DOGE";  // "DOGE" or "LTC"
    public long Height { get; set; }
    public string Hash { get; set; } = "";
    public string Worker { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public bool Confirmed { get; set; }
    public int QubicEpoch { get; set; }
    public decimal DogePriceUsdAtFind { get; set; }  // DOGE/USD price at the moment this block was first recorded
    public decimal LtcPriceUsdAtFind { get; set; }   // LTC/USD price at the moment this block was first recorded (only for LTC blocks)
}

// Live stats from pool.json (not persisted, just passed through to frontend)
public class PoolLiveStats
{
    public DateTimeOffset SessionStart { get; set; }
    public int SharesValid { get; set; }
    public int SharesInvalid { get; set; }
    public double SharesPerMinute { get; set; }
    // DOGE (auxiliary chain)
    public int BlocksFound { get; set; }
    public int BlocksConfirmed { get; set; }
    public DateTimeOffset? LastBlockTime { get; set; }
    public long? LastBlockHeight { get; set; }
    // LTC (primary chain)
    public int LtcBlocksFound { get; set; }
    public int LtcBlocksConfirmed { get; set; }
    public DateTimeOffset? LtcLastBlockTime { get; set; }
    public long? LtcLastBlockHeight { get; set; }
    public DateTimeOffset? LastShareTime { get; set; }
}

// pool.json API response model — matches https://doge-stats.qubic.org/pool.json
public class PoolJsonResponse
{
    public long Uptime { get; set; }
    public PoolJsonChains? Chains { get; set; }
    public long CurrentHeight { get; set; }
    public long CurrentHeightAuxiliary { get; set; }
    public PoolJsonNetwork? Network { get; set; }
    public PoolJsonShares Shares { get; set; } = new();
    public PoolJsonBlocksRoot Blocks { get; set; } = new();
    public DateTimeOffset? LastShare { get; set; }
    public PoolJsonLastBlock? LastBlock { get; set; }
    public PoolJsonLastBlock? LastBlockAuxiliary { get; set; }
    public List<PoolJsonBlock> RecentBlocks { get; set; } = [];
}

public class PoolJsonChains
{
    public string Primary { get; set; } = "";
    public string Auxiliary { get; set; } = "";
}

public class PoolJsonNetwork
{
    public PoolJsonNetworkChain? Primary { get; set; }
    public PoolJsonNetworkChain? Auxiliary { get; set; }
}

public class PoolJsonNetworkChain
{
    public string Coin { get; set; } = "";
    public double Difficulty { get; set; }
    public long Hashrate { get; set; }
}

public class PoolJsonShares
{
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public double PerMinute { get; set; }
}

public class PoolJsonBlocksRoot
{
    public PoolJsonBlocksChain? Primary { get; set; }
    public PoolJsonBlocksChain? Auxiliary { get; set; }
    // Legacy root-level fields (equals primary)
    public int Found { get; set; }
    public int Confirmed { get; set; }
}

public class PoolJsonBlocksChain
{
    public string Coin { get; set; } = "";
    public int Found { get; set; }
    public int Confirmed { get; set; }
}

public class PoolJsonLastBlock
{
    public string Coin { get; set; } = "";
    public long Height { get; set; }
    public DateTimeOffset Time { get; set; }
}

public class PoolJsonBlock
{
    public string Chain { get; set; } = "";   // "primary" (LTC) or "auxiliary" (DOGE)
    public string Coin { get; set; } = "";    // "LTC" or "DOGE"
    public long Height { get; set; }
    public string Hash { get; set; } = "";
    public string Worker { get; set; } = "";
    public string Payout { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public bool Confirmed { get; set; }
}

// DOGE network stats from Dogecoin explorer (blockchair.com)
public class DogeNetworkStats
{
    public long NetworkHashrate { get; set; }   // H/s (24h average)
    public long BestBlockHeight { get; set; }   // current chain tip
    public DateTimeOffset FetchedAt { get; set; }
}

// DOGE price from CoinPaprika
public class DogePriceStats
{
    public decimal UsdPrice { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

// QU (Qubic) price from CoinPaprika
public class QuPriceStats
{
    public decimal UsdPrice { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

// LTC (Litecoin) price from CoinPaprika
public class LtcPriceStats
{
    public decimal UsdPrice { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

// Aggregated summary for one completed Qubic epoch — persisted separately so raw snapshots can be deleted later
public class EpochSummary
{
    [BsonId(autoId: true)]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public int EpochNumber { get; set; }
    public DateTimeOffset EpochStart { get; set; }
    public DateTimeOffset EpochEnd { get; set; }

    // Hashrate
    public long AvgHashrate { get; set; }
    public string AvgHashrateDisplay { get; set; } = "";
    public long PeakHashrate { get; set; }
    public string PeakHashrateDisplay { get; set; } = "";
    public DateTimeOffset PeakHashrateAt { get; set; }

    // Pool share of network (%)
    public double PeakNetworkSharePct { get; set; }
    public DateTimeOffset PeakNetworkShareAt { get; set; }

    // Counters (delta over the epoch — current minus baseline at epoch start)
    public int TotalPoolAccepted { get; set; }
    public int TotalPoolRejected { get; set; }
    public int TotalSolutionsAccepted { get; set; }
    public int TotalSolutionsStale { get; set; }
    public int TotalTasksDistributed { get; set; }

    // Baseline counter values at epoch start — used to compute live deltas without a full DB scan
    public int BaselinePoolAccepted { get; set; }
    public int BaselinePoolRejected { get; set; }
    public int BaselineSolutionsAccepted { get; set; }
    public int BaselineSolutionsStale { get; set; }
    public int BaselineTasksDistributed { get; set; }
    public int BaselineSharesValid { get; set; }
    public bool BaselineSet { get; set; }

    // DOGE blocks (from pool_blocks collection)
    public int BlocksFound { get; set; }
    public int BlocksConfirmed { get; set; }
    public int SharesValid { get; set; }

    // LTC blocks (from pool_blocks collection, Chain == "LTC")
    public int LtcBlocksFound { get; set; }
    public int LtcBlocksConfirmed { get; set; }

    public bool IsFinalized { get; set; }  // true once epoch has ended

    // DOGE mining pool rank (lower = better; 0 = not yet recorded)
    public int BestRank { get; set; }
    public DateTimeOffset BestRankAt { get; set; }
}

// All-time aggregated stats across all epochs — one record, upserted each epoch-end
public class AllTimeStats
{
    [BsonId]
    public int Id { get; set; } = 1; // singleton
    public DateTimeOffset UpdatedAt { get; set; }

    public long PeakHashrate { get; set; }
    public string PeakHashrateDisplay { get; set; } = "";
    public int PeakHashrateEpoch { get; set; }
    public DateTimeOffset PeakHashrateAt { get; set; }

    public double PeakNetworkSharePct { get; set; }
    public int PeakNetworkShareEpoch { get; set; }
    public DateTimeOffset PeakNetworkShareAt { get; set; }

    // Cumulative totals across all finalized epochs
    public int TotalPoolAccepted { get; set; }
    public int TotalPoolRejected { get; set; }
    public int TotalSolutionsAccepted { get; set; }
    public int TotalSolutionsStale { get; set; }
    public int TotalTasksDistributed { get; set; }
    public int TotalSharesValid { get; set; }
    public int TotalLtcBlocksFound { get; set; }
    public int TotalLtcBlocksConfirmed { get; set; }

    // Peak per-epoch counter values (highest single-epoch value ever recorded)
    public int PeakPoolAccepted { get; set; }
    public int PeakPoolAcceptedEpoch { get; set; }
    public int PeakPoolRejected { get; set; }
    public int PeakPoolRejectedEpoch { get; set; }
    public int PeakSolutionsAccepted { get; set; }
    public int PeakSolutionsAcceptedEpoch { get; set; }
    public int PeakSolutionsStale { get; set; }
    public int PeakSolutionsStaleEpoch { get; set; }
    public int PeakTasksDistributed { get; set; }
    public int PeakTasksDistributedEpoch { get; set; }
    public int PeakBlocksFound { get; set; }
    public int PeakBlocksFoundEpoch { get; set; }
    public int PeakBlocksConfirmed { get; set; }
    public int PeakBlocksConfirmedEpoch { get; set; }
    public int PeakSharesValid { get; set; }
    public int PeakSharesValidEpoch { get; set; }
    public int PeakLtcBlocksFound { get; set; }
    public int PeakLtcBlocksFoundEpoch { get; set; }

    // DOGE mining pool rank (lower = better; 0 = not yet recorded)
    public int BestRank { get; set; }
    public int BestRankEpoch { get; set; }
    public DateTimeOffset BestRankAt { get; set; }
}

public class MiningPoolEntry
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public double HashrateGHs { get; set; }   // GH/s for display
}

public class MiningPoolRanking
{
    public MiningPoolEntry? Above { get; set; }   // null if qubic is #1
    public MiningPoolEntry Qubic { get; set; } = new();
    public MiningPoolEntry? Below { get; set; }   // null if qubic is last
    public int TotalPools { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

// ASIC miner profitability data from whattomine.com
public class AsicMinerData
{
    public string Name { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public double HashrateGHs { get; set; }  // GH/s
    public int PowerWatts { get; set; }       // W
    public double Revenue24hUsd { get; set; } // at whattomine default power cost
    public DateTimeOffset FetchedAt { get; set; }

    // Calculated client-side based on user's power cost
    public double Profit24hUsd(double powerCostKwh) =>
        Revenue24hUsd - (PowerWatts / 1000.0 * 24 * powerCostKwh);

    public double BreakevenKwh =>
        Revenue24hUsd / (PowerWatts / 1000.0 * 24);
}

public class AsicProfitabilityData
{
    public List<AsicMinerData> Miners { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }
}

public class DonationEntry
{
    [BsonId(autoId: true)]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public string Address { get; set; } = "";
    public long AmountQu { get; set; }
    public DateTime Date { get; set; }
}

public class TopDonor
{
    public string Address { get; set; } = "";
    public long TotalQu { get; set; }
    public string Date { get; set; } = "";
}

// ── Visitor Analytics ────────────────────────────────────────────────────────

public class VisitorEntry
{
    [BsonId(autoId: true)]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string IpHash { get; set; } = "";  // SHA256 hash of IP, no raw IP stored
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string Path { get; set; } = "/";
}

public class VisitorStatsDto
{
    public int TotalPageViews { get; set; }
    public int UniqueVisitorsToday { get; set; }
    public int UniqueVisitorsThisWeek { get; set; }
    public int UniqueVisitorsThisMonth { get; set; }
    public int ActiveVisitorsLastHour { get; set; }
    public List<DailyVisitorCount> Last30Days { get; set; } = new();
    public List<MonthlyVisitorCount> Last24Months { get; set; } = new();
    public List<YearlyVisitorCount> AllYears { get; set; } = new();
    public List<CountryCount> TopCountries30Days { get; set; } = new();
    public List<CountryCount> TopCountriesAllTime { get; set; } = new();
}

public class DailyVisitorCount
{
    public string Date { get; set; } = "";  // "yyyy-MM-dd"
    public int PageViews { get; set; }
    public int UniqueVisitors { get; set; }
}

public class MonthlyVisitorCount
{
    public string Month { get; set; } = "";  // "yyyy-MM"
    public int PageViews { get; set; }
    public int UniqueVisitors { get; set; }
}

public class YearlyVisitorCount
{
    public int Year { get; set; }
    public int PageViews { get; set; }
    public int UniqueVisitors { get; set; }
}

public class CountryCount
{
    public string CountryCode { get; set; } = "";
    public string CountryName { get; set; } = "";
    public int Count { get; set; }
}

// Qubic RPC v2 response models for address transfers
// Actual structure: GET /v2/identities/{id}/transfers
public class QubicTransferResponse
{
    public List<QubicTickGroup> Transactions { get; set; } = [];
}

public class QubicTickGroup
{
    public long TickNumber { get; set; }
    public string Identity { get; set; } = "";
    public List<QubicTransactionEntry> Transactions { get; set; } = [];
}

public class QubicTransactionEntry
{
    public QubicTransactionDetail Transaction { get; set; } = new();
    public long Timestamp { get; set; }  // Unix ms
    public bool MoneyFlew { get; set; }
}

public class QubicTransactionDetail
{
    public string SourceId { get; set; } = "";
    public string DestId { get; set; } = "";
    public long Amount { get; set; }
    public long TickNumber { get; set; }
    public int InputType { get; set; }
}

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

// PoolBlock — one confirmed or pending DOGE block find, stored permanently
public class PoolBlock
{
    [BsonId(autoId: true)]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public long Height { get; set; }
    public string Hash { get; set; } = "";
    public string Worker { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public bool Confirmed { get; set; }
    public int QubicEpoch { get; set; }
    public decimal DogePriceUsdAtFind { get; set; }  // DOGE/USD price at the moment this block was first recorded
}

// Live stats from pool.json (not persisted, just passed through to frontend)
public class PoolLiveStats
{
    public DateTimeOffset SessionStart { get; set; }
    public int SharesValid { get; set; }
    public int SharesInvalid { get; set; }
    public int BlocksFound { get; set; }
    public int BlocksConfirmed { get; set; }
    public DateTimeOffset? LastShareTime { get; set; }
    public DateTimeOffset? LastBlockTime { get; set; }
    public long? LastBlockHeight { get; set; }
}

// pool.json API response model — matches real API structure
// {"uptime":34933,"shares":{"valid":61283,"invalid":129},"blocks":{"found":0,"confirmed":0},
//  "lastShare":"2026-04-01T10:31:24.050Z","lastBlock":null,"recentBlocks":[]}
public class PoolJsonResponse
{
    public long Uptime { get; set; }
    public PoolJsonShares Shares { get; set; } = new();
    public PoolJsonBlocks Blocks { get; set; } = new();
    public DateTimeOffset? LastShare { get; set; }
    public PoolJsonLastBlock? LastBlock { get; set; }
    public List<PoolJsonBlock> RecentBlocks { get; set; } = [];
}

public class PoolJsonShares
{
    public int Valid { get; set; }
    public int Invalid { get; set; }
}

public class PoolJsonBlocks
{
    public int Found { get; set; }
    public int Confirmed { get; set; }
}

public class PoolJsonLastBlock
{
    public long Height { get; set; }
    public DateTimeOffset Time { get; set; }
}

public class PoolJsonBlock
{
    public long Height { get; set; }
    public string Hash { get; set; } = "";
    public string Worker { get; set; } = "";
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
    public bool BaselineSet { get; set; }

    // DOGE blocks (from pool_blocks collection)
    public int BlocksFound { get; set; }
    public int BlocksConfirmed { get; set; }
    public int SharesValid { get; set; }

    public bool IsFinalized { get; set; }  // true once epoch has ended
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
    public int TotalBlocksFound { get; set; }
    public int TotalBlocksConfirmed { get; set; }
    public int TotalSharesValid { get; set; }

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
}

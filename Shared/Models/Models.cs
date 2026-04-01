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

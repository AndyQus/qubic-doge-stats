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

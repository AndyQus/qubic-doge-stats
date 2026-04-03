using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class DogeStatsPollingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DogeStatsPollingWorker> _logger;
    private readonly int _intervalSeconds;

    // Epoch cache — shared across polls (worker is singleton-lifetime as hosted service)
    private int _cachedEpoch = 0;
    private int _previousEpoch = 0;
    private DateTimeOffset _epochFetchedAt = DateTimeOffset.MinValue;

    // Shared epoch for use by other workers (e.g. PoolPollingWorker)
    public static int CurrentEpoch => _sharedEpoch;
    private static int _sharedEpoch = 0;

    public DogeStatsPollingWorker(IServiceProvider services, ILogger<DogeStatsPollingWorker> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _intervalSeconds = config.GetValue("DogeStats:PollIntervalSeconds", 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DogeStats polling worker started. Interval: {Interval}s", _intervalSeconds);

        await PollAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAsync(stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<DogeStatsClient>();
            var rpc = scope.ServiceProvider.GetRequiredService<QubicRpcClient>();
            var db = scope.ServiceProvider.GetRequiredService<LiteDbContext>();
            var epochSvc = scope.ServiceProvider.GetRequiredService<EpochSummaryService>();

            var response = await client.FetchAsync(ct);
            if (response is null) return;

            // Refresh epoch from RPC when needed
            if (ShouldRefreshEpoch())
            {
                var tickInfo = await rpc.GetTickInfoAsync(ct);
                if (tickInfo?.TickInfo is not null)
                {
                    var newEpoch = tickInfo.TickInfo.Epoch;
                    if (newEpoch != _cachedEpoch && _cachedEpoch != 0)
                    {
                        _logger.LogInformation("Qubic epoch changed: {Old} → {New}", _cachedEpoch, newEpoch);
                        _previousEpoch = _cachedEpoch;
                    }

                    _cachedEpoch = newEpoch;
                    _sharedEpoch = newEpoch;
                    _epochFetchedAt = DateTimeOffset.UtcNow;
                    _logger.LogDebug("Epoch refreshed: {Epoch}", _cachedEpoch);
                }
            }

            var networkHashrate = DogeExplorerPollingWorker.LatestStats?.NetworkHashrate ?? 0;

            var snapshot = new HashrateSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                QubicEpoch = _cachedEpoch,
                Hashrate = response.Mining.Hashrate,
                HashrateDisplay = response.Mining.HashrateDisplay,
                PoolDifficulty = response.Mining.PoolDifficulty,
                TasksDistributed = response.Mining.TasksDistributed,
                ActiveTasks = response.ActiveTasks,
                ConnectedPeers = response.Network.ConnectedPeers,
                TotalPeers = response.Network.TotalPeers,
                PoolAccepted = response.Pool.Accepted,
                PoolRejected = response.Pool.Rejected,
                PoolSubmitted = response.Pool.Submitted,
                SolutionsAccepted = response.Solutions.Accepted,
                SolutionsReceived = response.Solutions.Received,
                SolutionsRejected = response.Solutions.Rejected,
                SolutionsStale = response.Solutions.Stale,
                UptimeSeconds = response.UptimeSeconds,
                QueueSolutions = response.Queues.Solutions,
                QueueStratum = response.Queues.Stratum,
                NetworkHashrate = networkHashrate
            };

            db.InsertSnapshot(snapshot);
            _logger.LogDebug("Snapshot saved: {Hashrate} (Epoch {Epoch})", snapshot.HashrateDisplay, snapshot.QubicEpoch);

            // Finalize summary for the just-ended epoch
            if (_previousEpoch > 0)
            {
                epochSvc.FinalizeEpoch(_previousEpoch);
                _previousEpoch = 0;
            }

            // Update current epoch peaks + deltas live on every poll
            epochSvc.UpdateLive(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during polling");
        }
    }

    /// <summary>
    /// Returns true if the epoch should be re-fetched from the Qubic RPC API.
    /// During the Wednesday 11:00–15:00 UTC transition window, checks on every poll.
    /// Otherwise, refreshes every 5 minutes.
    /// </summary>
    private bool ShouldRefreshEpoch()
    {
        if (_cachedEpoch == 0) return true;

        var now = DateTimeOffset.UtcNow;

        // Wednesday epoch transition window: 11:00–15:00 UTC → check every poll
        if (now.DayOfWeek == DayOfWeek.Wednesday)
        {
            var minuteOfDay = now.Hour * 60 + now.Minute;
            if (minuteOfDay >= 660 && minuteOfDay <= 900)
                return true;
        }

        return (now - _epochFetchedAt).TotalMinutes >= 5;
    }
}

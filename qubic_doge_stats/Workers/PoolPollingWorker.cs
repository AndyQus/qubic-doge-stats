using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class PoolPollingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PoolPollingWorker> _logger;

    public static PoolLiveStats? LatestStats { get; private set; }

    public PoolPollingWorker(IServiceProvider services, ILogger<PoolPollingWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<PoolStatsClient>();
            var db = scope.ServiceProvider.GetRequiredService<LiteDbContext>();

            var response = await client.FetchAsync(ct);
            if (response is null)
            {
                _logger.LogWarning("pool.json fetch returned no data");
                return;
            }

            var currentPrice = DogePricePollingWorker.LatestPrice?.UsdPrice ?? 0m;

            var currentEpoch = DogeStatsPollingWorker.CurrentEpoch;

            // Only persist blocks once the epoch is known — saves with epoch=0 would be
            // invisible to GetBlocksFoundByEpoch() and corrupt epoch summaries.
            // Blocks are deduplicated by Height so they will be picked up on the next poll.
            if (currentEpoch > 0)
            {
                foreach (var rb in response.RecentBlocks)
                {
                    db.UpsertPoolBlock(new PoolBlock
                    {
                        Height = rb.Height,
                        Hash = rb.Hash,
                        Worker = rb.Worker,
                        Time = rb.Time,
                        Confirmed = rb.Confirmed,
                        QubicEpoch = currentEpoch,
                        DogePriceUsdAtFind = currentPrice
                    });
                }

                // Fallback: recentBlocks can be empty if the block is older than the pool's sliding window.
                // Use lastBlock to ensure it is always persisted (hash/worker will be empty until recentBlocks catches up).
                if (response.LastBlock is { } lb &&
                    response.RecentBlocks.All(rb => rb.Height != lb.Height))
                {
                    db.UpsertPoolBlock(new PoolBlock
                    {
                        Height = lb.Height,
                        Hash = "",
                        Worker = "",
                        Time = lb.Time,
                        Confirmed = response.Blocks.Confirmed > 0,
                        QubicEpoch = currentEpoch,
                        DogePriceUsdAtFind = currentPrice
                    });
                }
            }
            else
            {
                _logger.LogWarning("Epoch not yet initialized — skipping pool block DB write (will retry next poll)");
            }

            LatestStats = new PoolLiveStats
            {
                SessionStart = DateTimeOffset.UtcNow.AddSeconds(-response.Uptime),
                SharesValid = response.Shares.Valid,
                SharesInvalid = response.Shares.Invalid,
                BlocksFound = response.Blocks.Found,
                BlocksConfirmed = response.Blocks.Confirmed,
                LastShareTime = response.LastShare,
                LastBlockTime = response.LastBlock?.Time,
                LastBlockHeight = response.LastBlock?.Height
            };

            _logger.LogDebug("Pool stats updated: {BlocksFound} blocks, {Shares} shares", response.Blocks.Found, response.Shares.Valid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during pool polling");
        }
    }

}

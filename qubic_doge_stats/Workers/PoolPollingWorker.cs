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
        try
        {
            await PollAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PollAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
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

            var currentDogePrice = DogePricePollingWorker.LatestPrice?.UsdPrice ?? 0m;
            var currentLtcPrice  = LtcPricePollingWorker.LatestPrice?.UsdPrice ?? 0m;

            var currentEpoch = DogeStatsPollingWorker.CurrentEpoch;

            // Only persist blocks once the epoch is known — saves with epoch=0 would be
            // invisible to GetBlocksFoundByEpoch() and corrupt epoch summaries.
            // Blocks are deduplicated by (Chain, Height) so they will be picked up on the next poll.
            if (currentEpoch > 0)
            {
                foreach (var rb in response.RecentBlocks)
                {
                    var isDoge = !string.Equals(rb.Coin, "LTC", StringComparison.OrdinalIgnoreCase);
                    var chain  = isDoge ? "DOGE" : "LTC";
                    db.UpsertPoolBlock(new PoolBlock
                    {
                        Chain = chain,
                        Height = rb.Height,
                        Hash = rb.Hash,
                        Worker = rb.Worker,
                        Time = rb.Time,
                        Confirmed = rb.Confirmed,
                        QubicEpoch = currentEpoch,
                        DogePriceUsdAtFind = isDoge ? currentDogePrice : 0m,
                        LtcPriceUsdAtFind  = isDoge ? 0m : currentLtcPrice
                    });
                }

                // Fallback for DOGE lastBlock
                if (response.LastBlockAuxiliary is { } lbDoge &&
                    response.RecentBlocks.All(rb => !(rb.Coin == "DOGE" && rb.Height == lbDoge.Height)))
                {
                    db.UpsertPoolBlock(new PoolBlock
                    {
                        Chain = "DOGE",
                        Height = lbDoge.Height,
                        Hash = "",
                        Worker = "",
                        Time = lbDoge.Time,
                        Confirmed = response.Blocks.Auxiliary?.Confirmed > 0,
                        QubicEpoch = currentEpoch,
                        DogePriceUsdAtFind = currentDogePrice
                    });
                }

                // Fallback for LTC lastBlock
                if (response.LastBlock is { } lbLtc &&
                    string.Equals(lbLtc.Coin, "LTC", StringComparison.OrdinalIgnoreCase) &&
                    response.RecentBlocks.All(rb => !(rb.Coin == "LTC" && rb.Height == lbLtc.Height)))
                {
                    db.UpsertPoolBlock(new PoolBlock
                    {
                        Chain = "LTC",
                        Height = lbLtc.Height,
                        Hash = "",
                        Worker = "",
                        Time = lbLtc.Time,
                        Confirmed = response.Blocks.Primary?.Confirmed > 0,
                        QubicEpoch = currentEpoch,
                        LtcPriceUsdAtFind = currentLtcPrice
                    });
                }
            }
            else
            {
                _logger.LogWarning("Epoch not yet initialized — skipping pool block DB write (will retry next poll)");
            }

            var dogeBlocks = response.Blocks.Auxiliary;
            var ltcBlocks  = response.Blocks.Primary;

            LatestStats = new PoolLiveStats
            {
                SessionStart      = DateTimeOffset.UtcNow.AddSeconds(-response.Uptime),
                SharesValid       = response.Shares.Valid,
                SharesInvalid     = response.Shares.Invalid,
                SharesPerMinute   = response.Shares.PerMinute,
                // DOGE
                BlocksFound       = dogeBlocks?.Found    ?? response.Blocks.Found,
                BlocksConfirmed   = dogeBlocks?.Confirmed ?? response.Blocks.Confirmed,
                LastBlockTime     = response.LastBlockAuxiliary?.Time ?? response.LastBlock?.Time,
                LastBlockHeight   = response.LastBlockAuxiliary?.Height ?? response.LastBlock?.Height,
                // LTC
                LtcBlocksFound    = ltcBlocks?.Found    ?? 0,
                LtcBlocksConfirmed = ltcBlocks?.Confirmed ?? 0,
                LtcLastBlockTime  = response.LastBlock?.Coin == "LTC" ? response.LastBlock.Time : null,
                LtcLastBlockHeight = response.LastBlock?.Coin == "LTC" ? response.LastBlock.Height : null,
                LastShareTime     = response.LastShare
            };

            _logger.LogDebug("Pool stats updated: {BlocksFound} blocks, {Shares} shares", response.Blocks.Found, response.Shares.Valid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during pool polling");
        }
    }

}

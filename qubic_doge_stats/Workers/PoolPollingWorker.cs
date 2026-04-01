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

            foreach (var rb in response.RecentBlocks)
            {
                db.UpsertPoolBlock(new PoolBlock
                {
                    Height = rb.Height,
                    Hash = rb.Hash,
                    Worker = rb.Worker,
                    Time = rb.Time,
                    Confirmed = rb.Confirmed,
                    QubicEpoch = DeriveEpoch(rb.Time)
                });
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

    // Epoch 180 reference: 2026-03-26 12:00 UTC (Wednesday). Each epoch = 7 days.
    private static int DeriveEpoch(DateTimeOffset time)
    {
        var epoch180Start = new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero);
        var weeksSince = (time - epoch180Start).TotalDays / 7.0;
        return 180 + (int)Math.Floor(weeksSince);
    }
}

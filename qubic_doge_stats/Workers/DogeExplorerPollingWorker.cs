using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class DogeExplorerPollingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DogeExplorerPollingWorker> _logger;

    public static DogeNetworkStats? LatestStats { get; private set; }

    public DogeExplorerPollingWorker(IServiceProvider services, ILogger<DogeExplorerPollingWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<DogeExplorerClient>();
            var stats = await client.FetchAsync(ct);
            if (stats is null)
            {
                _logger.LogWarning("DOGE explorer fetch returned no data");
                return;
            }
            LatestStats = stats;
            _logger.LogDebug("DOGE network hashrate updated: {Hashrate:N0} H/s", stats.NetworkHashrate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DOGE explorer polling");
        }
    }
}

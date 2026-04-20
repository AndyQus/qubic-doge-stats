using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class QuPricePollingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<QuPricePollingWorker> _logger;

    public static QuPriceStats? LatestPrice { get; private set; }

    public QuPricePollingWorker(IServiceProvider services, ILogger<QuPricePollingWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<QuPriceClient>();
            var price = await client.FetchAsync(ct);
            if (price is null)
            {
                _logger.LogWarning("QU price fetch returned no data");
                return;
            }
            LatestPrice = price;
            _logger.LogDebug("QU price updated: ${Usd:F6} USD", price.UsdPrice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during QU price polling");
        }
    }
}

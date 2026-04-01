using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class DogePricePollingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DogePricePollingWorker> _logger;

    public static DogePriceStats? LatestPrice { get; private set; }

    public DogePricePollingWorker(IServiceProvider services, ILogger<DogePricePollingWorker> logger)
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
            var client = scope.ServiceProvider.GetRequiredService<DogePriceClient>();
            var price = await client.FetchAsync(ct);
            if (price is null)
            {
                _logger.LogWarning("DOGE price fetch returned no data");
                return;
            }
            LatestPrice = price;
            _logger.LogDebug("DOGE price updated: ${Usd:F4} USD", price.UsdPrice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DOGE price polling");
        }
    }
}

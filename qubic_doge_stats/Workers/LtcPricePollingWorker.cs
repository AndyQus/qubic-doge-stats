using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class LtcPricePollingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LtcPricePollingWorker> _logger;

    public static LtcPriceStats? LatestPrice { get; private set; }

    public LtcPricePollingWorker(IServiceProvider services, ILogger<LtcPricePollingWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PollAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
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
            var client = scope.ServiceProvider.GetRequiredService<LtcPriceClient>();
            var price = await client.FetchAsync(ct);
            if (price is null)
            {
                _logger.LogWarning("LTC price fetch returned no data");
                return;
            }
            LatestPrice = price;
            _logger.LogDebug("LTC price updated: ${Usd:F2} USD", price.UsdPrice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LTC price polling");
        }
    }
}

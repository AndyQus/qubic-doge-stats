using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class MiningPoolRankingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MiningPoolRankingWorker> _logger;

    public static MiningPoolRanking? LatestRanking { get; private set; }

    public MiningPoolRankingWorker(IServiceProvider services, ILogger<MiningPoolRankingWorker> logger)
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
            var client = scope.ServiceProvider.GetRequiredService<MiningPoolRankingClient>();
            var ranking = await client.FetchAsync(ct);
            if (ranking is not null)
            {
                LatestRanking = ranking;
                _logger.LogDebug("Pool ranking updated: qubic is #{Rank} of {Total}", ranking.Qubic.Rank, ranking.TotalPools);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during mining pool ranking poll");
        }
    }
}

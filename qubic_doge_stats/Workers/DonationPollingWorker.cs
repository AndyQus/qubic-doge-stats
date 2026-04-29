using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Services;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Workers;

public class DonationPollingWorker : BackgroundService
{
    private const string DonationAddress = "CCCJKFMDTUFFWDCRBFNHMQRYOBABEKBDUZWEJMARUETQPTFZWBCJLYUGREXI";

    private readonly IServiceProvider _services;
    private readonly ILogger<DonationPollingWorker> _logger;

    public DonationPollingWorker(IServiceProvider services, ILogger<DonationPollingWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var rpc = scope.ServiceProvider.GetRequiredService<QubicRpcClient>();
            var db = scope.ServiceProvider.GetRequiredService<LiteDbContext>();

            var transfers = await rpc.GetAddressTransfersAsync(DonationAddress, ct);
            if (transfers?.Transactions is null || transfers.Transactions.Count == 0)
                return;

            var lastTick = db.GetLastDonationTick();
            long maxTick = lastTick;
            int newCount = 0;

            // v2 response: transactions[tickGroup].transactions[tx]
            foreach (var tickGroup in transfers.Transactions)
            {
                foreach (var entry in tickGroup.Transactions)
                {
                    if (!entry.MoneyFlew) continue;
                    if (entry.Transaction.DestId != DonationAddress) continue;
                    if (entry.Transaction.InputType != 0) continue;
                    if (entry.Transaction.TickNumber <= lastTick) continue;
                    if (entry.Transaction.Amount <= 0) continue;

                    var date = entry.Timestamp > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(entry.Timestamp).UtcDateTime
                        : DateTime.UtcNow;

                    db.InsertDonationIfNew(new DonationEntry
                    {
                        Address = entry.Transaction.SourceId,
                        AmountQu = entry.Transaction.Amount,
                        Date = date.Date
                    });

                    if (entry.Transaction.TickNumber > maxTick)
                        maxTick = entry.Transaction.TickNumber;
                    newCount++;
                }
            }

            if (maxTick > lastTick)
                db.SetLastDonationTick(maxTick);

            _logger.LogDebug("Donation poll done. {New} new donations from {Groups} tick groups",
                newCount, transfers.Transactions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during donation polling");
        }
    }
}

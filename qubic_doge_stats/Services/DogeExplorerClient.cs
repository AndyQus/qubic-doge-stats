using qubic_doge_stats.Shared.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace qubic_doge_stats.Services;

public class DogeExplorerClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DogeExplorerClient> _logger;

    public DogeExplorerClient(HttpClient http, ILogger<DogeExplorerClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<DogeNetworkStats?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync("", ct);
            var root = JsonSerializer.Deserialize<BlockchairDogeResponse>(json);
            var hashrate = root?.Data?.Hashrate24h;
            if (hashrate is null) return null;

            return new DogeNetworkStats
            {
                NetworkHashrate = hashrate.Value,
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch DOGE network stats from blockchair");
            return null;
        }
    }

    // blockchair.com response models
    private class BlockchairDogeResponse
    {
        [JsonPropertyName("data")]
        public BlockchairDogeData? Data { get; set; }
    }

    private class BlockchairDogeData
    {
        [JsonPropertyName("hashrate_24h")]
        public long? Hashrate24h { get; set; }
    }
}

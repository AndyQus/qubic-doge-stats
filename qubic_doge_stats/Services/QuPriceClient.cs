using System.Text.Json;
using System.Text.Json.Serialization;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Services;

public class QuPriceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<QuPriceClient> _logger;

    public QuPriceClient(HttpClient http, ILogger<QuPriceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<QuPriceStats?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync("", ct);
            var root = JsonSerializer.Deserialize<CoinPaprikaResponse>(json);
            var usd = root?.Quotes?.Usd?.Price;
            if (usd is null) return null;

            return new QuPriceStats
            {
                UsdPrice = usd.Value,
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch QU price from CoinPaprika");
            return null;
        }
    }

    private class CoinPaprikaResponse
    {
        [JsonPropertyName("quotes")]
        public CoinPaprikaQuotes? Quotes { get; set; }
    }

    private class CoinPaprikaQuotes
    {
        [JsonPropertyName("USD")]
        public CoinPaprikaUsd? Usd { get; set; }
    }

    private class CoinPaprikaUsd
    {
        [JsonPropertyName("price")]
        public decimal? Price { get; set; }
    }
}

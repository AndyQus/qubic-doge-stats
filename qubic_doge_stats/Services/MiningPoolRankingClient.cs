using qubic_doge_stats.Shared.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace qubic_doge_stats.Services;

public class MiningPoolRankingClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MiningPoolRankingClient> _logger;

    public MiningPoolRankingClient(HttpClient http, ILogger<MiningPoolRankingClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<MiningPoolRanking?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            // Step 1: fetch the main page to get the current valid cache-bust timestamp.
            // Cloudflare only allows data requests with timestamps it has already served.
            var html = await _http.GetStringAsync("https://miningpoolstats.stream/dogecoin", ct);
            var match = Regex.Match(html, @"last_time\s*=\s*""(\d+)""");
            if (!match.Success)
            {
                _logger.LogWarning("Could not extract last_time from miningpoolstats.stream page");
                return null;
            }
            var ts = match.Groups[1].Value;

            // Step 2: fetch pool data using the extracted timestamp
            var json = await _http.GetStringAsync($"https://data.miningpoolstats.stream/data/dogecoin.js?t={ts}", ct);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<MiningPoolStatsResponse>(json, options);
            if (response?.Data is null || response.Data.Count == 0) return null;

            var sorted = response.Data.OrderByDescending(p => p.Hashrate).ToList();
            int qubicIdx = sorted.FindIndex(p => p.Url != null && p.Url.Contains("qubic", StringComparison.OrdinalIgnoreCase));
            if (qubicIdx < 0) return null;

            MiningPoolEntry ToEntry(MiningPoolStatsPool p, int idx) => new()
            {
                Rank = idx + 1,
                Name = ExtractName(p.Url),
                HashrateGHs = p.Hashrate / 1_000_000_000.0
            };

            return new MiningPoolRanking
            {
                Above = qubicIdx > 0 ? ToEntry(sorted[qubicIdx - 1], qubicIdx - 1) : null,
                Qubic = ToEntry(sorted[qubicIdx], qubicIdx),
                Below = qubicIdx < sorted.Count - 1 ? ToEntry(sorted[qubicIdx + 1], qubicIdx + 1) : null,
                TotalPools = sorted.Count,
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch mining pool ranking");
            return null;
        }
    }

    private static string ExtractName(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "?";
        if (url.Contains("qubic", StringComparison.OrdinalIgnoreCase)) return "qubic.org";
        try
        {
            var host = new Uri(url).Host;
            return host.StartsWith("www.") ? host[4..] : host;
        }
        catch { return url; }
    }
}

// Internal deserialization models (not shared)
internal class MiningPoolStatsResponse
{
    public List<MiningPoolStatsPool> Data { get; set; } = [];
}

internal class MiningPoolStatsPool
{
    public string? Url { get; set; }
    public long Hashrate { get; set; }
}

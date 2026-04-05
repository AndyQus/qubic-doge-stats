using qubic_doge_stats.Shared.Models;
using System.Text.Json;

namespace qubic_doge_stats.Client.Services;

public class MiningPoolRankingService
{
    private readonly HttpClient _http;
    private readonly ILogger<MiningPoolRankingService> _logger;

    public MiningPoolRankingService(HttpClient http, ILogger<MiningPoolRankingService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<MiningPoolRanking?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var json = await _http.GetStringAsync($"https://data.miningpoolstats.stream/data/dogecoin.js?t={ts}", ct);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<MpsResponse>(json, options);
            if (response?.Data is null || response.Data.Count == 0) return null;

            var sorted = response.Data.OrderByDescending(p => p.Hashrate).ToList();
            int idx = sorted.FindIndex(p => p.Url != null && p.Url.Contains("qubic", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null;

            MiningPoolEntry ToEntry(MpsPool p, int i) => new()
            {
                Rank = i + 1,
                Name = ExtractHost(p.Url),
                HashrateGHs = p.Hashrate / 1_000_000_000.0
            };

            return new MiningPoolRanking
            {
                Above = idx > 0 ? ToEntry(sorted[idx - 1], idx - 1) : null,
                Qubic = ToEntry(sorted[idx], idx),
                Below = idx < sorted.Count - 1 ? ToEntry(sorted[idx + 1], idx + 1) : null,
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

    private static string ExtractHost(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "?";
        try
        {
            var host = new Uri(url).Host;
            return host.StartsWith("www.") ? host[4..] : host;
        }
        catch { return url; }
    }

    private class MpsResponse { public List<MpsPool> Data { get; set; } = []; }
    private class MpsPool { public string? Url { get; set; } public long Hashrate { get; set; } }
}

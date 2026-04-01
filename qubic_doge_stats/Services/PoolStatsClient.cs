using qubic_doge_stats.Shared.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace qubic_doge_stats.Services;

public class PoolStatsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PoolStatsClient> _logger;

    public PoolStatsClient(HttpClient http, ILogger<PoolStatsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PoolJsonResponse?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return await _http.GetFromJsonAsync<PoolJsonResponse>("", options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch pool.json");
            return null;
        }
    }
}

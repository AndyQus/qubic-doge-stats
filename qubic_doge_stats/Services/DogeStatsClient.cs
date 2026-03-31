using qubic_doge_stats.Shared.Models;
using System.Text.Json;

namespace qubic_doge_stats.Services;

public class DogeStatsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DogeStatsClient> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public DogeStatsClient(HttpClient http, ILogger<DogeStatsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<DispatcherResponse?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync("", ct);
            return JsonSerializer.Deserialize<DispatcherResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch doge stats");
            return null;
        }
    }
}

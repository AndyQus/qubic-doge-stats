using qubic_doge_stats.Shared.Models;
using System.Text.Json;

namespace qubic_doge_stats.Services;

public class QubicRpcClient
{
    private readonly HttpClient _http;
    private readonly ILogger<QubicRpcClient> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public QubicRpcClient(HttpClient http, ILogger<QubicRpcClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<QubicTickInfoResponse?> GetTickInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync("v1/tick-info", ct);
            return JsonSerializer.Deserialize<QubicTickInfoResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Qubic tick info");
            return null;
        }
    }
}

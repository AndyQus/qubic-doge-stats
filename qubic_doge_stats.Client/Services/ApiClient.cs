using qubic_doge_stats.Shared.Models;
using System.Net.Http.Json;

namespace qubic_doge_stats.Client.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    public async Task<HashrateSnapshot?> GetLatestSnapshotAsync()
    {
        try { return await _http.GetFromJsonAsync<HashrateSnapshot>("/api/snapshots/latest"); }
        catch { return null; }
    }

    public async Task<List<HashrateSnapshot>> GetHistoryAsync(int limit = 100)
    {
        try { return await _http.GetFromJsonAsync<List<HashrateSnapshot>>($"/api/snapshots/history?limit={limit}") ?? []; }
        catch { return []; }
    }

    public async Task<PoolLiveStats?> GetPoolStatsAsync()
    {
        try { return await _http.GetFromJsonAsync<PoolLiveStats>("/api/pool/latest"); }
        catch { return null; }
    }

    public async Task<List<PoolBlock>> GetPoolBlocksAsync()
    {
        try { return await _http.GetFromJsonAsync<List<PoolBlock>>("/api/pool/blocks") ?? []; }
        catch { return []; }
    }

    public async Task<DogeNetworkStats?> GetNetworkStatsAsync()
    {
        try { return await _http.GetFromJsonAsync<DogeNetworkStats>("/api/network/stats"); }
        catch { return null; }
    }

    public async Task<DogePriceStats?> GetDogePriceAsync()
    {
        try { return await _http.GetFromJsonAsync<DogePriceStats>("/api/doge/price"); }
        catch { return null; }
    }
}

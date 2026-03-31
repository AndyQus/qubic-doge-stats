using LiteDB;
using qubic_doge_stats.Shared.Models;

namespace qubic_doge_stats.Infrastructure;

public class LiteDbContext : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly object _lock = new();

    public LiteDbContext(IConfiguration configuration)
    {
        var filename = configuration["LiteDb:Filename"] ?? "Data/doge_stats.db";
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
        {
            var dbFile = Environment.GetEnvironmentVariable("LITEDB_FILE") ?? "doge_stats.db";
            filename = Path.Combine(dataDir, dbFile);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
        _db = new LiteDatabase(filename);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var col = _db.GetCollection<HashrateSnapshot>("snapshots");
        col.EnsureIndex(x => x.Timestamp);
    }

    public List<HashrateSnapshot> GetSnapshots(int limit = 1440)
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .Query()
                .OrderByDescending(x => x.Timestamp)
                .Limit(limit)
                .ToList();
        }
    }

    public HashrateSnapshot? GetLatestSnapshot()
    {
        lock (_lock)
        {
            return _db.GetCollection<HashrateSnapshot>("snapshots")
                .Query()
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefault();
        }
    }

    public void InsertSnapshot(HashrateSnapshot snapshot)
    {
        lock (_lock)
        {
            _db.GetCollection<HashrateSnapshot>("snapshots").Insert(snapshot);
        }
    }

    public void Dispose() => _db.Dispose();
}

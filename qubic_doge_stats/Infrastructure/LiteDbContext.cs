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
        // One-time cleanup: remove any fake blocks left from development phase
        _db.GetCollection("pool_blocks").DeleteMany("$.IsFake = true");
    }

    private void EnsureIndexes()
    {
        var col = _db.GetCollection<HashrateSnapshot>("snapshots");
        col.EnsureIndex(x => x.Timestamp);

        var poolCol = _db.GetCollection<PoolBlock>("pool_blocks");
        poolCol.EnsureIndex(x => x.Height);
        poolCol.EnsureIndex(x => x.QubicEpoch);
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

    public void UpsertPoolBlock(PoolBlock block)
    {
        lock (_lock)
        {
            var col = _db.GetCollection<PoolBlock>("pool_blocks");
            var existing = col.FindOne(x => x.Hash == block.Hash);
            if (existing is null)
                col.Insert(block);
            else
            {
                block.Id = existing.Id;
                col.Update(block);
            }
        }
    }

    public List<PoolBlock> GetAllPoolBlocks()
    {
        lock (_lock)
        {
            return _db.GetCollection<PoolBlock>("pool_blocks")
                .Query()
                .OrderByDescending(x => x.Time)
                .ToList();
        }
    }

    public void Dispose() => _db.Dispose();
}

using LiteDB;
using qubic_doge_stats.Shared.Models;

var dbPath = @"c:\Softwareentwicklung\AI\qubic_doge_stats\qubic_doge_stats\Data\doge_stats.db";
using var db = new LiteDatabase($"Filename={dbPath};Connection=Shared");

var col = db.GetCollection<PoolBlock>("pool_blocks");
var blocks = col.FindAll().OrderBy(b => b.Time).ToList();

// Qubic epoch schedule: each epoch = exactly 7 days, starting Wednesday 12:00 UTC
// Epoch 208 started 2026-04-08 12:00 UTC (current)
const int currentEpoch = 208;
const int minMiningEpoch = 207;

// Compute start of current epoch
static DateTimeOffset GetEpochStartUtc(DateTimeOffset reference)
{
    var refUtc = reference.UtcDateTime;
    int daysSinceWed = ((int)refUtc.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
    var lastWed = refUtc.Date.AddDays(-daysSinceWed);
    var candidate = new DateTimeOffset(lastWed, TimeSpan.Zero).AddHours(12);
    if (candidate > reference) candidate = candidate.AddDays(-7);
    return candidate;
}

var currentEpochStart = GetEpochStartUtc(DateTimeOffset.UtcNow);
Console.WriteLine($"Aktuelle Epoche: {currentEpoch}, Start: {currentEpochStart:yyyy-MM-dd HH:mm} UTC");
Console.WriteLine("─────────────────────────────────────────────────────");

int fixedCount = 0;
foreach (var block in blocks)
{
    int weeksBack;
    if (block.Time >= currentEpochStart)
        weeksBack = 0;
    else
        weeksBack = (int)Math.Floor((currentEpochStart - block.Time).TotalDays / 7.0) + 1;

    var correctEpoch = currentEpoch - weeksBack;
    if (correctEpoch < minMiningEpoch) continue;

    Console.Write($"  Block {block.Height}  Zeit={block.Time:yyyy-MM-dd HH:mm}  Alt=E{block.QubicEpoch}  Korrekt=E{correctEpoch}");

    if (block.QubicEpoch != correctEpoch)
    {
        block.QubicEpoch = correctEpoch;
        col.Update(block);
        fixedCount++;
        Console.WriteLine("  ← KORRIGIERT");
    }
    else
    {
        Console.WriteLine("  OK");
    }
}

Console.WriteLine("─────────────────────────────────────────────────────");
Console.WriteLine($"Korrigiert: {fixedCount}/{blocks.Count} Blöcke");
Console.WriteLine();

// Zusammenfassung nach Fix
var updated = col.FindAll().OrderBy(b => b.Time).ToList();
foreach (var g in updated.GroupBy(b => b.QubicEpoch).OrderBy(g => g.Key))
    Console.WriteLine($"  E{g.Key}: {g.Count()} Blöcke");

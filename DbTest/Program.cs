using LiteDB;
using qubic_doge_stats.Shared.Models;

// ──────────────────────────────────────────────────────────────────────────────
// DB Repair for Epoch 213 block classification bug
//
// Problem: at the start of E213, before v0.4.0 LTC support was fully active,
// some LTC blocks were stored with Chain="DOGE" (wrong height range < 4_000_000).
// Additionally, a few legitimate DOGE blocks are duplicated in the DB.
//
// Fix:
//   Step 1: Wrong DOGE entries (height < 4M = LTC range)
//           - If a correct LTC entry already exists → DELETE the wrong DOGE copy
//           - If no LTC entry exists → CONVERT to LTC (fix Chain, swap price fields)
//   Step 2: Deduplicate DOGE blocks (same Chain+Height+Epoch stored twice)
//   Step 3: Rebuild EpochSummary block counts from corrected pool_blocks
// ──────────────────────────────────────────────────────────────────────────────

var dbPath = args.Length > 0 ? args[0]
    : @"c:\Softwareentwicklung\AI\qubic_doge_stats\qubic_doge_stats\Data\doge_stats.db";

Console.WriteLine($"DB: {dbPath}");
using var db = new LiteDatabase($"Filename={dbPath};Connection=Shared");

var blockCol   = db.GetCollection<PoolBlock>("pool_blocks");
var summaryCol = db.GetCollection<EpochSummary>("epoch_summaries");

var blocks = blockCol.FindAll().OrderBy(b => b.Time).ToList();

Console.WriteLine($"=== DB Repair: pool_blocks ({blocks.Count} total) ===");
Console.WriteLine();

// ── STEP 1: Fix LTC blocks misclassified as DOGE (Height < 4_000_000 = LTC range)
Console.WriteLine("--- Step 1: Fix LTC blocks stored as DOGE ---");
var wrongDogeAsLtc = blocks.Where(b => b.Chain == "DOGE" && b.Height < 4_000_000).ToList();
Console.WriteLine($"Found {wrongDogeAsLtc.Count} DOGE-labelled blocks with LTC heights (< 4,000,000)");

int step1Fixed = 0, step1Deleted = 0;
foreach (var wrong in wrongDogeAsLtc)
{
    var existingLtc = blockCol.FindOne(x => x.Chain == "LTC" && x.Height == wrong.Height);
    if (existingLtc is not null)
    {
        blockCol.Delete(wrong.Id);
        Console.WriteLine($"  DELETED wrong DOGE copy height={wrong.Height} (correct LTC entry already exists)");
        step1Deleted++;
    }
    else
    {
        wrong.Chain = "LTC";
        // Transfer price if stored in DogePriceUsdAtFind by mistake
        if (wrong.LtcPriceUsdAtFind == 0 && wrong.DogePriceUsdAtFind > 0)
            wrong.LtcPriceUsdAtFind = wrong.DogePriceUsdAtFind;
        wrong.DogePriceUsdAtFind = 0m;
        blockCol.Update(wrong);
        Console.WriteLine($"  CONVERTED height={wrong.Height} DOGE -> LTC");
        step1Fixed++;
    }
}
Console.WriteLine($"  Step 1: {step1Fixed} converted, {step1Deleted} deleted");
Console.WriteLine();

// ── STEP 2: Remove duplicate DOGE blocks (same Chain+Height+Epoch)
Console.WriteLine("--- Step 2: Remove DOGE duplicates ---");
blocks = blockCol.FindAll().OrderBy(b => b.Time).ToList();
var seen = new HashSet<string>();
int step2Deleted = 0;
foreach (var block in blocks)
{
    var key = $"{block.Chain}:{block.Height}:{block.QubicEpoch}";
    if (!seen.Add(key))
    {
        blockCol.Delete(block.Id);
        Console.WriteLine($"  DELETED duplicate {block.Chain} height={block.Height} epoch={block.QubicEpoch}");
        step2Deleted++;
    }
}
Console.WriteLine($"  Step 2: {step2Deleted} duplicates removed");
Console.WriteLine();

// ── STEP 3: Rebuild EpochSummary block counts from corrected pool_blocks
Console.WriteLine("--- Step 3: Rebuild EpochSummary block counts ---");
blocks = blockCol.FindAll().ToList();
var summaries = summaryCol.FindAll().ToList();
foreach (var summary in summaries.OrderBy(s => s.EpochNumber))
{
    var dogeFound     = blocks.Count(b => b.QubicEpoch == summary.EpochNumber && b.Chain == "DOGE");
    var dogeConfirmed = blocks.Count(b => b.QubicEpoch == summary.EpochNumber && b.Chain == "DOGE" && b.Confirmed);
    var ltcFound      = blocks.Count(b => b.QubicEpoch == summary.EpochNumber && b.Chain == "LTC");
    var ltcConfirmed  = blocks.Count(b => b.QubicEpoch == summary.EpochNumber && b.Chain == "LTC" && b.Confirmed);

    bool changed = summary.BlocksFound != dogeFound || summary.BlocksConfirmed != dogeConfirmed ||
                   summary.LtcBlocksFound != ltcFound || summary.LtcBlocksConfirmed != ltcConfirmed;

    Console.WriteLine($"  E{summary.EpochNumber}: " +
                      $"DOGE {summary.BlocksFound}->{dogeFound} (conf: {summary.BlocksConfirmed}->{dogeConfirmed})  " +
                      $"LTC {summary.LtcBlocksFound}->{ltcFound} (conf: {summary.LtcBlocksConfirmed}->{ltcConfirmed})  " +
                      (changed ? "UPDATED" : "OK"));

    summary.BlocksFound      = dogeFound;
    summary.BlocksConfirmed  = dogeConfirmed;
    summary.LtcBlocksFound      = ltcFound;
    summary.LtcBlocksConfirmed  = ltcConfirmed;
    summaryCol.Upsert(summary);
}
Console.WriteLine();

// ── Final verification
Console.WriteLine("=== Final State ===");
blocks = blockCol.FindAll().OrderBy(b => b.Time).ToList();
Console.WriteLine($"Total blocks in DB: {blocks.Count}");
foreach (var g in blocks.GroupBy(b => b.QubicEpoch).OrderBy(g => g.Key))
{
    var doge = g.Where(b => b.Chain == "DOGE").ToList();
    var ltc  = g.Where(b => b.Chain == "LTC").ToList();
    Console.WriteLine($"  E{g.Key}: DOGE={doge.Count} (conf={doge.Count(b => b.Confirmed)})  " +
                      $"LTC={ltc.Count} (conf={ltc.Count(b => b.Confirmed)})");
}
Console.WriteLine();
Console.WriteLine("Done. Restart the application server to rebuild AllTimeStats from corrected data.");

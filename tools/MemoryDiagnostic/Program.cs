using System.Diagnostics;
using System.Globalization;
using Asynkron.JsEngine;

#pragma warning disable MA0047
#pragma warning disable MA0048

Console.WriteLine("=== Asynkron.JsEngine Memory Diagnostic (Deep) ===");
Console.WriteLine($"Process: {Environment.ProcessId}");
Console.WriteLine();

// ── Baseline ──────────────────────────────────────────────
ForceGC();
var baseline = GC.GetTotalMemory(true);
PrintMem("Baseline (empty process)", baseline, 0);

// ── 1. Create bare engine (no JS executed) ───────────────
await using var engine1 = new JsEngine();
ForceGC();
var afterEngineCreate = GC.GetTotalMemory(true);
PrintMem("After JsEngine() construction", afterEngineCreate, baseline);

// ── 2. Execute trivial script ────────────────────────────
await engine1.Evaluate("1 + 1");
ForceGC();
var afterTrivial = GC.GetTotalMemory(true);
PrintMem("After trivial eval '1+1'", afterTrivial, afterEngineCreate);

// ── 3. Exercise RegExp (basic) ───────────────────────────
await engine1.Evaluate("var re = /abc/g; re.test('xabcx');");
ForceGC();
var afterBasicRegex = GC.GetTotalMemory(true);
PrintMem("After basic regex /abc/g", afterBasicRegex, afterTrivial);

// ── 4. Exercise RegExp with unicode property escape ──────
await engine1.Evaluate(@"
var re1 = /\p{General_Category=Letter}/u;
re1.test('A');
");
ForceGC();
var afterPropEscape1 = GC.GetTotalMemory(true);
PrintMem("After 1st property escape (GC=Letter)", afterPropEscape1, afterBasicRegex);

// ── 5. More property escapes (triggers cache population) ─
await engine1.Evaluate(@"
var re2 = /\p{Script=Arabic}/u; re2.test('\u0627');
var re3 = /\p{Script=Han}/u; re3.test('\u4e00');
var re4 = /\p{Script=Cyrillic}/u; re4.test('\u0410');
var re5 = /\p{Script=Devanagari}/u; re5.test('\u0905');
var re6 = /\p{Script=Greek}/u; re6.test('\u0391');
");
ForceGC();
var afterMultiPropEscape = GC.GetTotalMemory(true);
PrintMem("After 5 more Script= property escapes", afterMultiPropEscape, afterPropEscape1);

// ── 6. Temporal (decomposed) ─────────────────────────────
Console.WriteLine();
Console.WriteLine("── Temporal Decomposition ──");

ForceGC();
var preTemporal = GC.GetTotalMemory(true);

await engine1.Evaluate("Temporal.Now.instant();");
ForceGC();
var afterTemporalInstant = GC.GetTotalMemory(true);
PrintMem("  Temporal.Now.instant()", afterTemporalInstant, preTemporal);

await engine1.Evaluate("Temporal.PlainDate.from('2024-01-15');");
ForceGC();
var afterTemporalDate = GC.GetTotalMemory(true);
PrintMem("  Temporal.PlainDate.from()", afterTemporalDate, afterTemporalInstant);

await engine1.Evaluate("Temporal.PlainTime.from('10:30:00');");
ForceGC();
var afterTemporalTime = GC.GetTotalMemory(true);
PrintMem("  Temporal.PlainTime.from()", afterTemporalTime, afterTemporalDate);

await engine1.Evaluate("Temporal.PlainDateTime.from('2024-01-15T10:30:00');");
ForceGC();
var afterTemporalDT = GC.GetTotalMemory(true);
PrintMem("  Temporal.PlainDateTime.from()", afterTemporalDT, afterTemporalTime);

await engine1.Evaluate("Temporal.Duration.from({ hours: 1, minutes: 30 });");
ForceGC();
var afterTemporalDur = GC.GetTotalMemory(true);
PrintMem("  Temporal.Duration.from()", afterTemporalDur, afterTemporalDT);

await engine1.Evaluate("Temporal.ZonedDateTime.from('2024-01-15T10:30:00[America/New_York]');");
ForceGC();
var afterTemporalZDT = GC.GetTotalMemory(true);
PrintMem("  Temporal.ZonedDateTime.from()", afterTemporalZDT, afterTemporalDur);

await engine1.Evaluate("Temporal.PlainYearMonth.from('2024-01');");
ForceGC();
var afterTemporalYM = GC.GetTotalMemory(true);
PrintMem("  Temporal.PlainYearMonth.from()", afterTemporalYM, afterTemporalZDT);

await engine1.Evaluate("Temporal.PlainMonthDay.from('01-15');");
ForceGC();
var afterTemporalMD = GC.GetTotalMemory(true);
PrintMem("  Temporal.PlainMonthDay.from()", afterTemporalMD, afterTemporalYM);

Console.WriteLine();
PrintMem("  Total Temporal cost", afterTemporalMD, preTemporal);

// ── 7. Compiled regex stress (10 unique patterns) ────────
Console.WriteLine();
await engine1.Evaluate(@"
var patterns = [];
for (var i = 0; i < 10; i++) {
  patterns.push(new RegExp('test' + i + '[a-z]+\\d{1,3}', 'g'));
}
for (var p of patterns) p.test('test5abc123');
");
ForceGC();
var afterRegexStress = GC.GetTotalMemory(true);
PrintMem("After 10 compiled regex patterns", afterRegexStress, afterTemporalMD);

// ── 8. Heavy unicode property escape suite ───────────────
Console.WriteLine();
Console.WriteLine("── Property Escape Decomposition ──");

ForceGC();
var preHeavy = GC.GetTotalMemory(true);

var heavyScripts = new[] {
    ("Arabic", @"/\p{Script_Extensions=Arabic}/u"),
    ("Bengali", @"/\p{Script_Extensions=Bengali}/u"),
    ("Ethiopic", @"/\p{Script_Extensions=Ethiopic}/u"),
    ("Georgian", @"/\p{Script_Extensions=Georgian}/u"),
    ("Hangul", @"/\p{Script_Extensions=Hangul}/u"),
    ("Katakana", @"/\p{Script_Extensions=Katakana}/u"),
    ("Latin", @"/\p{Script_Extensions=Latin}/u"),
    ("Tamil", @"/\p{Script_Extensions=Tamil}/u"),
    ("Thai", @"/\p{Script_Extensions=Thai}/u"),
    ("Tibetan", @"/\p{Script_Extensions=Tibetan}/u"),
};

var prevMem = preHeavy;
foreach (var (name, pattern) in heavyScripts)
{
    await engine1.Evaluate($"var _{name} = {pattern}; _{name}.test('A');");
    ForceGC();
    var cur = GC.GetTotalMemory(true);
    PrintMem($"  Script_Extensions={name}", cur, prevMem);
    prevMem = cur;
}

Console.WriteLine();
PrintMem("  Total Script_Extensions= cost", prevMem, preHeavy);

// ── 9. Binary property escapes ───────────────────────────
Console.WriteLine();
Console.WriteLine("── Binary Property Escape Decomposition ──");

ForceGC();
var preBinary = GC.GetTotalMemory(true);

var binaryProps = new[] {
    ("Alphabetic", @"/\p{Alphabetic}/u"),
    ("Emoji", @"/\p{Emoji}/u"),
    ("Lowercase", @"/\p{Lowercase}/u"),
    ("Uppercase", @"/\p{Uppercase}/u"),
    ("White_Space", @"/\p{White_Space}/u"),
    ("ID_Start", @"/\p{ID_Start}/u"),
    ("ID_Continue", @"/\p{ID_Continue}/u"),
    ("Assigned", @"/\p{Assigned}/u"),
    ("ASCII", @"/\p{ASCII}/u"),
    ("Any", @"/\p{Any}/u"),
};

prevMem = preBinary;
foreach (var (name, pattern) in binaryProps)
{
    await engine1.Evaluate($"var _bp_{name} = {pattern}; _bp_{name}.test('A');");
    ForceGC();
    var cur = GC.GetTotalMemory(true);
    PrintMem($"  {name}", cur, prevMem);
    prevMem = cur;
}

Console.WriteLine();
PrintMem("  Total binary property cost", prevMem, preBinary);

// ── 10. Test with second engine (shared statics) ─────────
Console.WriteLine();
Console.WriteLine("── Second Engine (tests static sharing) ──");
ForceGC();
var preSecondEngine = GC.GetTotalMemory(true);

await using var engine2 = new JsEngine();
await engine2.Evaluate("1+1");
ForceGC();
var afterSecondEngine = GC.GetTotalMemory(true);
PrintMem("2nd JsEngine() + trivial eval", afterSecondEngine, preSecondEngine);

await engine2.Evaluate(@"
var re = /\p{Script=Arabic}/u; re.test('\u0627');
Temporal.Now.instant();
");
ForceGC();
var afterSecondEngineFeatures = GC.GetTotalMemory(true);
PrintMem("2nd engine regex+temporal (cached)", afterSecondEngineFeatures, afterSecondEngine);

// ── Summary ──────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ TOTAL SUMMARY ═══");
PrintMem("Total heap growth (baseline to end)", afterSecondEngineFeatures, baseline);
Console.WriteLine();

Console.WriteLine("── Major cost centers ──");
PrintMem("  Engine construction (1st)", afterEngineCreate, baseline);
PrintMem("  Temporal API total", afterTemporalMD, preTemporal);
PrintMem("  Property escapes (all)", prevMem, afterBasicRegex);
PrintMem("  2nd engine (marginal cost)", afterSecondEngineFeatures, preSecondEngine);

Console.WriteLine();
Console.WriteLine($"GC Gen0 collections: {GC.CollectionCount(0)}");
Console.WriteLine($"GC Gen1 collections: {GC.CollectionCount(1)}");
Console.WriteLine($"GC Gen2 collections: {GC.CollectionCount(2)}");

var gcInfo = GC.GetGCMemoryInfo();
Console.WriteLine($"Heap size: {gcInfo.HeapSizeBytes / 1024.0 / 1024.0:F2} MB");
Console.WriteLine($"Total committed: {gcInfo.TotalCommittedBytes / 1024.0 / 1024.0:F2} MB");
Console.WriteLine($"Fragmented: {gcInfo.FragmentedBytes / 1024.0 / 1024.0:F2} MB");

static void ForceGC()
{
    GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Aggressive, true, true);
}

static void PrintMem(string label, long current, long reference)
{
    var delta = current - reference;
    var deltaStr = delta >= 0 ? $"+{delta / 1024.0 / 1024.0:F2}" : $"{delta / 1024.0 / 1024.0:F2}";
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  {label,-48} {current / 1024.0 / 1024.0,8:F2} MB  ({deltaStr} MB)"));
}

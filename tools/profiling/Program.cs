using System.Diagnostics;
using System.Globalization;
using Asynkron.JsEngine;

// Harness: regExpUtils.js buildString + testPropertyEscapes
const string harness = """
function buildString(args) {
  const loneCodePoints = args.loneCodePoints;
  const ranges = args.ranges;
  const CHUNK_SIZE = 10000;
  let result = String.fromCodePoint.apply(null, loneCodePoints);
  for (let i = 0; i < ranges.length; i++) {
    let range = ranges[i];
    let start = range[0];
    let end = range[1];
    let codePoints = [];
    for (let length = 0, codePoint = start; codePoint <= end; codePoint++) {
      codePoints[length++] = codePoint;
      if (length === CHUNK_SIZE) {
        result += String.fromCodePoint.apply(null, codePoints);
        codePoints.length = length = 0;
      }
    }
    result += String.fromCodePoint.apply(null, codePoints);
  }
  return result;
}

function printCodePoint(codePoint) {
  const hex = codePoint.toString(16).toUpperCase().padStart(6, "0");
  return 'U+' + hex;
}

function testPropertyEscapes(regExp, string, expression) {
  if (!regExp.test(string)) {
    for (let symbol of string) {
      let formatted = printCodePoint(symbol.codePointAt(0));
      if (!regExp.test(symbol)) {
        throw new Error(expression + ' should match ' + formatted);
      }
    }
  }
}
""";

// The actual Test262 test: Script_Extensions=Arabic
const string testCode = """
const matchSymbols = buildString({
  loneCodePoints: [
    0x00204F, 0x002E41, 0x00FDCF, 0x01EE24, 0x01EE27, 0x01EE39, 0x01EE3B,
    0x01EE42, 0x01EE47, 0x01EE49, 0x01EE4B, 0x01EE54, 0x01EE57, 0x01EE59,
    0x01EE5B, 0x01EE5D, 0x01EE5F, 0x01EE64, 0x01EE7E
  ],
  ranges: [
    [0x000600, 0x000604], [0x000606, 0x0006DC], [0x0006DE, 0x0006FF],
    [0x000750, 0x00077F], [0x000870, 0x00088E], [0x000890, 0x000891],
    [0x000897, 0x0008E1], [0x0008E3, 0x0008FF], [0x00FB50, 0x00FBC2],
    [0x00FBD3, 0x00FD8F], [0x00FD92, 0x00FDC7], [0x00FDF0, 0x00FDFF],
    [0x00FE70, 0x00FE74], [0x00FE76, 0x00FEFC], [0x0102E0, 0x0102FB],
    [0x010E60, 0x010E7E], [0x010EC2, 0x010EC4], [0x010EFC, 0x010EFF],
    [0x01EE00, 0x01EE03], [0x01EE05, 0x01EE1F], [0x01EE21, 0x01EE22],
    [0x01EE29, 0x01EE32], [0x01EE34, 0x01EE37], [0x01EE4D, 0x01EE4F],
    [0x01EE51, 0x01EE52], [0x01EE61, 0x01EE62], [0x01EE67, 0x01EE6A],
    [0x01EE6C, 0x01EE72], [0x01EE74, 0x01EE77], [0x01EE79, 0x01EE7C],
    [0x01EE80, 0x01EE89], [0x01EE8B, 0x01EE9B], [0x01EEA1, 0x01EEA3],
    [0x01EEA5, 0x01EEA9], [0x01EEAB, 0x01EEBB], [0x01EEF0, 0x01EEF1]
  ]
});

const nonMatchSymbols = buildString({
  loneCodePoints: [
    0x000605, 0x0006DD, 0x00088F, 0x0008E2, 0x00FE75, 0x01EE04, 0x01EE20,
    0x01EE23, 0x01EE28, 0x01EE33, 0x01EE38, 0x01EE3A, 0x01EE48, 0x01EE4A,
    0x01EE4C, 0x01EE50, 0x01EE53, 0x01EE58, 0x01EE5A, 0x01EE5C, 0x01EE5E,
    0x01EE60, 0x01EE63, 0x01EE6B, 0x01EE73, 0x01EE78, 0x01EE7D, 0x01EE7F,
    0x01EE8A, 0x01EEA4, 0x01EEAA
  ],
  ranges: [
    [0x00DC00, 0x00DFFF],
    [0x000000, 0x0005FF], [0x000700, 0x00074F], [0x000780, 0x00086F],
    [0x000892, 0x000896], [0x000900, 0x00204E], [0x002050, 0x002E40],
    [0x002E42, 0x00DBFF], [0x00E000, 0x00FB4F], [0x00FBC3, 0x00FBD2],
    [0x00FD90, 0x00FD91], [0x00FDC8, 0x00FDCE], [0x00FDD0, 0x00FDEF],
    [0x00FE00, 0x00FE6F], [0x00FEFD, 0x0102DF], [0x0102FC, 0x010E5F],
    [0x010E7F, 0x010EC1], [0x010EC5, 0x010EFB], [0x010F00, 0x01EDFF],
    [0x01EE25, 0x01EE26], [0x01EE3C, 0x01EE41], [0x01EE43, 0x01EE46],
    [0x01EE55, 0x01EE56], [0x01EE65, 0x01EE66], [0x01EE9C, 0x01EEA0],
    [0x01EEBC, 0x01EEEF], [0x01EEF2, 0x10FFFF]
  ]
});

// Phase 1: Build strings
"strings built";
""";

const string matchTest = """
testPropertyEscapes(
  /^\p{Script_Extensions=Arabic}+$/u,
  matchSymbols,
  "\\p{Script_Extensions=Arabic}"
);
"match test passed";
""";

const string nonMatchTest = """
testPropertyEscapes(
  /^\P{Script_Extensions=Arabic}+$/u,
  nonMatchSymbols,
  "\\P{Script_Extensions=Arabic}"
);
"non-match test passed";
""";

Console.WriteLine("=== PropertyEscape Profile ===");

await using var engine = new JsEngine();

var sw = Stopwatch.StartNew();
await engine.Evaluate(harness);
Console.WriteLine($"Harness load:    {sw.ElapsedMilliseconds}ms");

sw.Restart();
await engine.Evaluate("""new RegExp('^\\p{Any}+$', 'u').test("A");""");
Console.WriteLine($"Unicode data warm-up: {sw.ElapsedMilliseconds}ms");

// Run representative property escape smoke tests: small/large scripts, binary
// properties, and both positive and negated anchored forms.
var propertyProfiles = new[]
{
    new PropertyProfile(
        "Script=Latin",
        [0x0041, 0x00C0, 0x0100],
        [0x0627, 0x03A9, 0x1F600],
        20_000),
    new PropertyProfile(
        "Script_Extensions=Arabic",
        [0x0600, 0x0627, 0x10E60, 0x1EE00],
        [0x0041, 0x05FF, 0x1F600],
        20_000),
    new PropertyProfile(
        "White_Space",
        [0x0009, 0x0020, 0x00A0, 0x2028],
        [0x0041, 0x0030, 0x005F],
        30_000),
    new PropertyProfile(
        "XID_Continue",
        [0x0041, 0x0030, 0x005F, 0x0300],
        [0x0020, 0x002D, 0x1F600],
        20_000),
    new PropertyProfile(
        "Alphabetic",
        [0x0041, 0x03A9, 0x0905, 0x1D400],
        [0x0020, 0x0030, 0x1F600],
        20_000),
    new PropertyProfile(
        "Grapheme_Base",
        [0x0041, 0x0905, 0x1F600],
        [0x0300, 0xFE0F],
        20_000),
};

for (var i = 0; i < propertyProfiles.Length; i++)
{
    var profile = propertyProfiles[i];
    sw.Restart();
    try
    {
        var positivePattern = $"^\\\\p{{{profile.PropertyExpression}}}+$";
        var negativePattern = $"^\\\\P{{{profile.PropertyExpression}}}+$";
        var code = $$"""
            globalThis.__propertyProfileRegexes ??= [];
            var re = new RegExp('{{positivePattern}}', 'u');
            var neg = new RegExp('{{negativePattern}}', 'u');
            globalThis.__propertyProfileRegexes[{{i.ToString(CultureInfo.InvariantCulture)}}] = [re, neg];
            "compiled";
            """;
        await engine.Evaluate(code);
        Console.WriteLine($"  Compile {profile.PropertyExpression,-40} {sw.ElapsedMilliseconds}ms");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Compile {profile.PropertyExpression,-40} FAILED: {ex.Message}");
    }
}

const string sampleProfileHelpers = """
function buildSampleString(codePoints, repetitions) {
  let unit = String.fromCodePoint.apply(null, codePoints);
  let result = "";
  for (let i = 0; i < repetitions; i++) {
    result += unit;
  }
  return result;
}
""";

await engine.Evaluate(sampleProfileHelpers);

for (var i = 0; i < propertyProfiles.Length; i++)
{
    var profile = propertyProfiles[i];
    await BuildSampleProfileString(engine, sw, profile, i, negate: false);
    await BuildSampleProfileString(engine, sw, profile, i, negate: true);
    await RunSampleMatchProfile(engine, sw, profile, i, negate: false);
    await RunSampleMatchProfile(engine, sw, profile, i, negate: true);
}

static async Task BuildSampleProfileString(
    JsEngine engine,
    Stopwatch sw,
    PropertyProfile profile,
    int profileIndex,
    bool negate)
{
    var samples = negate ? profile.NonMatchSamples : profile.MatchSamples;
    var label = negate ? "Build negated" : "Build positive";
    var code = $$"""
        globalThis.__propertyProfileSamples ??= [];
        globalThis.__propertyProfileSamples[{{profileIndex.ToString(CultureInfo.InvariantCulture)}}] ??= [];
        globalThis.__propertyProfileSamples[{{profileIndex.ToString(CultureInfo.InvariantCulture)}}][{{(negate ? "1" : "0")}}] =
          buildSampleString(
            [{{FormatCodePoints(samples)}}],
            {{profile.Repetitions.ToString(CultureInfo.InvariantCulture)}});
        globalThis.__propertyProfileSamples[{{profileIndex.ToString(CultureInfo.InvariantCulture)}}][{{(negate ? "1" : "0")}}].length;
        """;

    sw.Restart();
    var result = await engine.Evaluate(code);
    Console.WriteLine(
        $"  {label,-15} {profile.PropertyExpression,-40} {sw.ElapsedMilliseconds}ms  length={result}");
}

static async Task RunSampleMatchProfile(
    JsEngine engine,
    Stopwatch sw,
    PropertyProfile profile,
    int profileIndex,
    bool negate)
{
    var label = negate ? "Negated sample" : "Positive sample";
    var code = $$"""
        var sampleRe = globalThis.__propertyProfileRegexes[{{profileIndex.ToString(CultureInfo.InvariantCulture)}}][{{(negate ? "1" : "0")}}];
        var sample = globalThis.__propertyProfileSamples[{{profileIndex.ToString(CultureInfo.InvariantCulture)}}][{{(negate ? "1" : "0")}}];
        if (!sampleRe.test(sample)) {
          throw new Error("sample did not match");
        }
        sample.length;
        """;

    sw.Restart();
    try
    {
        var result = await engine.Evaluate(code);
        Console.WriteLine(
            $"  {label,-15} {profile.PropertyExpression,-40} {sw.ElapsedMilliseconds}ms  length={result}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"  {label,-15} {profile.PropertyExpression,-40} {sw.ElapsedMilliseconds}ms  FAILED: {ex.Message}");
    }
}

// Full test with Arabic (buildString + match)
sw.Restart();
var r1 = await engine.Evaluate(testCode);
Console.WriteLine($"\nBuild strings (Arabic):   {sw.ElapsedMilliseconds}ms");

sw.Restart();
try
{
    var r2 = await engine.Evaluate(matchTest);
    Console.WriteLine($"Match test (Arabic):      {sw.ElapsedMilliseconds}ms  result={r2}");
}
catch (Exception ex)
{
    Console.WriteLine($"Match test (Arabic):      {sw.ElapsedMilliseconds}ms  FAILED: {ex.Message}");
}

sw.Restart();
try
{
    var r3 = await engine.Evaluate(nonMatchTest);
    Console.WriteLine($"Non-match test (Arabic):  {sw.ElapsedMilliseconds}ms  result={r3}");
}
catch (Exception ex)
{
    Console.WriteLine($"Non-match test (Arabic):  {sw.ElapsedMilliseconds}ms  FAILED: {ex.Message}");
}

Console.WriteLine("\nDone.");

static string FormatCodePoints(int[] codePoints)
{
    return string.Join(
        ", ",
        codePoints.Select(codePoint => "0x" + codePoint.ToString("X", CultureInfo.InvariantCulture)));
}

internal sealed record PropertyProfile(
    string PropertyExpression,
    int[] MatchSamples,
    int[] NonMatchSamples,
    int Repetitions);

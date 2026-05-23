using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Jint;
using Microsoft.Extensions.Logging;

#pragma warning disable MA0047 // Top-level statements live in script-style Program.
#pragma warning disable MA0048 // File name matches project entry point, not nested types.

const string listCommand = "list";
var engineKind = EngineKind.Asynkron;
var reportExpressionProgramStorage = false;
var reportStatementInstructionStorage = false;
var positionalArgs = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if (string.Equals(arg, "--jint", StringComparison.OrdinalIgnoreCase))
    {
        engineKind = EngineKind.Jint;
        continue;
    }

    if (string.Equals(arg, "--asynkron", StringComparison.OrdinalIgnoreCase))
    {
        engineKind = EngineKind.Asynkron;
        continue;
    }

    if (arg.StartsWith("--engine=", StringComparison.OrdinalIgnoreCase))
    {
        var engineValue = arg[(arg.IndexOf('=') + 1)..];
        if (!TryParseEngineKind(engineValue, out engineKind))
        {
            Console.Error.WriteLine($"Unknown engine: {engineValue}");
            return;
        }

        continue;
    }

    if (string.Equals(arg, "--engine", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("Missing value for --engine.");
            return;
        }

        if (!TryParseEngineKind(args[i + 1], out engineKind))
        {
            Console.Error.WriteLine($"Unknown engine: {args[i + 1]}");
            return;
        }

        i++;
        continue;
    }

    if (string.Equals(arg, "--expression-program-storage", StringComparison.OrdinalIgnoreCase))
    {
        reportExpressionProgramStorage = true;
        continue;
    }

    if (string.Equals(arg, "--statement-instruction-storage", StringComparison.OrdinalIgnoreCase))
    {
        reportStatementInstructionStorage = true;
        continue;
    }

    positionalArgs.Add(arg);
}

var profileKey = positionalArgs.Count > 0 ? positionalArgs[0] : "fib";
var repoRoot = FindRepoRoot();
var manifestPath = Path.Combine(repoRoot, "tools", "profile-manifest.json");
var manifest = LoadManifest(manifestPath);

if (string.Equals(profileKey, listCommand, StringComparison.OrdinalIgnoreCase))
{
    PrintProfiles(manifest);
    return;
}

if (string.Equals(profileKey, "bytecode", StringComparison.OrdinalIgnoreCase))
{
    RunBytecodeProfile();
    await Task.Delay(500);
    return;
}

if (!manifest.Profiles.TryGetValue(profileKey, out var profile))
{
    Console.Error.WriteLine($"Unknown profile: {profileKey}");
    Console.Error.WriteLine("Use 'list' to see available profiles.");
    return;
}

var scriptPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.ScriptsDir, profile.Script);
if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Script not found: {scriptPath}");
    return;
}

var script = File.ReadAllText(scriptPath);
var warmup = profile.Warmup > 0 ? profile.Warmup : 1;
var iterations = profile.Iterations > 0 ? profile.Iterations : 1;

var traceRealm = false;
var runsForAverage = iterations;
if (!string.IsNullOrWhiteSpace(profile.TraceRealmEnv))
{
    traceRealm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(profile.TraceRealmEnv));
    if (traceRealm && profile.TraceRealmRuns > 0)
    {
        runsForAverage = profile.TraceRealmRuns;
    }
}

if (!string.IsNullOrWhiteSpace(profile.Header))
{
    var header = profile.Header
        .Replace("{runs}", runsForAverage.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{traceRealm}", traceRealm.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    Console.WriteLine(header);
}

if (engineKind == EngineKind.Jint)
{
    if (profile.FreshEnginePerIteration)
    {
        RunWithFreshJintEngines(script, profile, warmup, iterations, runsForAverage);
    }
    else
    {
        RunWithSharedJintEngine(script, profile, warmup, iterations, runsForAverage);
    }
}
else if (profile.FreshEnginePerIteration)
{
    await RunWithFreshEnginesAsync(script, profile, traceRealm, warmup, iterations, runsForAverage);
}
else
{
    await RunWithSharedEnginesAsync(script, profile, traceRealm, warmup, iterations, runsForAverage);
}

// Give profiler time to flush data before exiting
await Task.Delay(500);
return;

async Task RunWithSharedEnginesAsync(
    string source,
    ProfileDefinition profile,
    bool traceRealm,
    int warmup,
    int iterations,
    int runsForAverage)
{
    await using var engine = CreateEngine(traceRealm);
    var parsed = engine.ParseProgram(source);
    if (reportExpressionProgramStorage)
    {
        PrintExpressionProgramStorage(profileKey, parsed);
    }

    if (reportStatementInstructionStorage)
    {
        PrintStatementInstructionStorage(profileKey, parsed);
    }

    for (var i = 0; i < warmup; i++)
    {
        await EvaluateAsync(engine, parsed);
    }

    var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
    for (var iter = 0; iter < iterations; iter++)
    {
        await EvaluateAsync(engine, parsed);
        if (profile.ShowProgress)
        {
            Console.Write(".");
        }
    }

    sw?.Stop();
    PrintCompletion(profile, sw?.ElapsedMilliseconds ?? 0, runsForAverage);
}

async Task RunWithFreshEnginesAsync(
    string source,
    ProfileDefinition profile,
    bool traceRealm,
    int warmup,
    int iterations,
    int runsForAverage)
{
    ProgramNode parsed;
    await using (var setupEngine = CreateEngine(traceRealm))
    {
        parsed = setupEngine.ParseProgram(source);
        if (reportExpressionProgramStorage)
        {
            PrintExpressionProgramStorage(profileKey, parsed);
        }

        if (reportStatementInstructionStorage)
        {
            PrintStatementInstructionStorage(profileKey, parsed);
        }
    }

    for (var i = 0; i < warmup; i++)
    {
        await using var warmEngine = CreateEngine(traceRealm);
        await EvaluateAsync(warmEngine, parsed);
    }

    var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
    for (var iter = 0; iter < iterations; iter++)
    {
        await using var engine = CreateEngine(traceRealm);
        await EvaluateAsync(engine, parsed);
        if (profile.ShowProgress)
        {
            Console.Write(".");
        }
    }

    sw?.Stop();
    PrintCompletion(profile, sw?.ElapsedMilliseconds ?? 0, runsForAverage);
}

async Task EvaluateAsync(JsEngine engine, ProgramNode parsed)
{
    try
    {
        await engine.Evaluate(parsed).WaitAsync(TimeSpan.FromSeconds(15));
    }
    catch (Exception x)
    {
        Console.WriteLine("Error: " + x);
    }
}

void RunWithSharedJintEngine(
    string source,
    ProfileDefinition profile,
    int warmup,
    int iterations,
    int runsForAverage)
{
    using var engine = CreateJintEngine();

    for (var i = 0; i < warmup; i++)
    {
        EvaluateJint(engine, source);
    }

    var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
    for (var iter = 0; iter < iterations; iter++)
    {
        EvaluateJint(engine, source);
        if (profile.ShowProgress)
        {
            Console.Write(".");
        }
    }

    sw?.Stop();
    PrintCompletion(profile, sw?.ElapsedMilliseconds ?? 0, runsForAverage);
}

void RunWithFreshJintEngines(
    string source,
    ProfileDefinition profile,
    int warmup,
    int iterations,
    int runsForAverage)
{
    for (var i = 0; i < warmup; i++)
    {
        using var warmEngine = CreateJintEngine();
        EvaluateJint(warmEngine, source);
    }

    var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
    for (var iter = 0; iter < iterations; iter++)
    {
        using var engine = CreateJintEngine();
        EvaluateJint(engine, source);
        if (profile.ShowProgress)
        {
            Console.Write(".");
        }
    }

    sw?.Stop();
    PrintCompletion(profile, sw?.ElapsedMilliseconds ?? 0, runsForAverage);
}

void EvaluateJint(Engine engine, string source)
{
    _ = engine.Evaluate(source).ToObject();
}

void RunBytecodeProfile()
{
    using var engine = CreateEngine(traceRealm: false);
    var environment = engine.GlobalEnvironment;
    var context = engine.RealmState.CreateContext(pushScope: false);
    var cases = CreateBytecodeCases();

    Console.WriteLine("BytecodeProfile");
    foreach (var profileCase in cases)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {profileCase.Name}: ops={profileCase.Program.OperationCount}, stack={profileCase.Program.MaxStackDepth}, iterations={profileCase.Iterations}"));
    }

    var checksum = 0d;
    foreach (var profileCase in cases)
    {
        checksum += RunBytecodeCase(profileCase, environment, context);
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Bytecode checksum: {checksum:F2}"));
}

double RunBytecodeCase(
    BytecodeProfileCase profileCase,
    JsEnvironment environment,
    EvaluationContext context)
{
    var sw = Stopwatch.StartNew();
    var checksum = TypedAstEvaluator.ProfileEvaluateLoweredExpressionProgramLoop(
        profileCase.Program,
        environment,
        context,
        profileCase.Iterations,
        profileCase.ExpectsNumericResult);
    sw.Stop();
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  {profileCase.Name}: {sw.ElapsedMilliseconds}ms"));
    return checksum;
}

static IReadOnlyList<BytecodeProfileCase> CreateBytecodeCases()
{
    return
    [
        new BytecodeProfileCase(
            "literal-add",
            CreateLiteralAddProgram(),
            Iterations: 2_000_000),
        new BytecodeProfileCase(
            "wide-arithmetic-64",
            CreateWideArithmeticProgram(64),
            Iterations: 300_000),
        new BytecodeProfileCase(
            "object-property-read",
            CreateObjectPropertyProgram(),
            Iterations: 800_000),
        new BytecodeProfileCase(
            "array-construction",
            CreateArrayConstructionProgram(8),
            Iterations: 500_000,
            ExpectsNumericResult: false)
    ];
}

static ExpressionProgram CreateLiteralAddProgram()
{
    return new ExpressionProgram(
        ImmutableArray.Create(
            PackedExpressionOp.LoadLiteralConstant(0),
            PackedExpressionOp.LoadLiteralConstant(1),
            PackedExpressionOp.Binary(BinaryOperator.Add)),
        literalConstants: ImmutableArray.Create(
            JsValue.FromDouble(40),
            JsValue.FromDouble(2)));
}

static ExpressionProgram CreateWideArithmeticProgram(int terms)
{
    var operations = ImmutableArray.CreateBuilder<PackedExpressionOp>(checked(terms * 2 - 1));
    var literals = ImmutableArray.CreateBuilder<JsValue>(terms);

    operations.Add(PackedExpressionOp.LoadLiteralConstant(0));
    literals.Add(JsValue.FromDouble(1));
    for (var i = 1; i < terms; i++)
    {
        operations.Add(PackedExpressionOp.LoadLiteralConstant(i));
        operations.Add(PackedExpressionOp.Binary(i % 3 == 0 ? BinaryOperator.Multiply : BinaryOperator.Add));
        literals.Add(JsValue.FromDouble(i + 1));
    }

    return new ExpressionProgram(
        operations.MoveToImmutable(),
        literalConstants: literals.MoveToImmutable());
}

static ExpressionProgram CreateObjectPropertyProgram()
{
    return new ExpressionProgram(
        ImmutableArray.Create(
            PackedExpressionOp.CreateObject,
            PackedExpressionOp.LoadLiteralConstant(0),
            PackedExpressionOp.DefineObjectProperty(0),
            PackedExpressionOp.GetNamedProperty(0)),
        literalConstants: ImmutableArray.Create(JsValue.FromDouble(123)),
        stringConstants: ImmutableArray.Create("value"));
}

static ExpressionProgram CreateArrayConstructionProgram(int itemCount)
{
    var operations = ImmutableArray.CreateBuilder<PackedExpressionOp>(checked(itemCount * 2 + 1));
    var literals = ImmutableArray.CreateBuilder<JsValue>(itemCount);

    operations.Add(PackedExpressionOp.CreateArray);
    for (var i = 0; i < itemCount; i++)
    {
        operations.Add(PackedExpressionOp.LoadLiteralConstant(i));
        operations.Add(PackedExpressionOp.ArrayPush);
        literals.Add(JsValue.FromDouble(i));
    }

    return new ExpressionProgram(
        operations.MoveToImmutable(),
        literalConstants: literals.MoveToImmutable());
}

void PrintCompletion(ProfileDefinition profile, long elapsedMs, int runsForAverage)
{
    if (!profile.ShowTiming)
    {
        Console.WriteLine("Done");
        return;
    }

    if (profile.ShowProgress)
    {
        Console.WriteLine();
    }

    var avgMs = runsForAverage > 0 ? elapsedMs / (double)runsForAverage : 0d;
    var elapsedText = elapsedMs.ToString(CultureInfo.InvariantCulture);
    var avgText = avgMs.ToString("F2", CultureInfo.InvariantCulture);
    Console.WriteLine($"Done in {elapsedText}ms (avg {avgText}ms per iteration)");
}

void PrintExpressionProgramStorage(string selectedProfileKey, ProgramNode program)
{
    var snapshot = ExpressionProgramStorageDiagnostics.Collect(program);
    Console.WriteLine("ExpressionProgram storage diagnostics:");
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  profile: {selectedProfileKey}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  programs: {snapshot.ProgramCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  total_ops: {snapshot.OperationCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  encoded_op_estimated_bytes: {snapshot.EstimatedEncodedOperationBytes}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  packed_op_shape: flags={snapshot.OperationsWithFlagsCount}, immediate0={snapshot.OperationsWithImmediate0Count}, immediate1={snapshot.OperationsWithImmediate1Count}, both_immediates={snapshot.OperationsWithBothImmediatesCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  constants: literals={snapshot.LiteralConstantCount}, strings={snapshot.StringConstantCount}, objects={snapshot.ObjectConstantCount}, identifiers={snapshot.IdentifierConstantCount}, spread_masks={snapshot.SpreadMaskConstantCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  optional_chain_shape: optional_ops={snapshot.OptionalOperationCount}, short_circuit_ops={snapshot.ShortCircuitOperationCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  side_state_estimates: max_stack_slots={snapshot.EstimatedMaxStackSlotCount}, stack_value_bytes={snapshot.EstimatedMaxStackValueBytes}, stack_flag_words={snapshot.EstimatedMaxStackFlagWordCount}, stack_flag_bytes={snapshot.EstimatedMaxStackFlagBytes}"));

    if (snapshot.OperationKindHistogram.IsDefaultOrEmpty)
    {
        Console.WriteLine("  op_kind_histogram: (empty)");
    }
    else
    {
        Console.WriteLine("  op_kind_histogram:");
        foreach (var (kind, count) in snapshot.OperationKindHistogram)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {kind}: {count}"));
        }
    }

    if (snapshot.MaxStackDepthHistogram.IsDefaultOrEmpty)
    {
        Console.WriteLine("  max_stack_depth_histogram: (empty)");
        return;
    }

    Console.WriteLine("  max_stack_depth_histogram:");
    foreach (var (depth, count) in snapshot.MaxStackDepthHistogram)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    depth={depth}: {count}"));
    }
}

void PrintStatementInstructionStorage(string selectedProfileKey, ProgramNode program)
{
    var snapshot = StatementInstructionStorageDiagnostics.Collect(program);
    Console.WriteLine("Statement instruction storage diagnostics:");
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  profile: {selectedProfileKey}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  plans: {snapshot.PlanCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  instructions: {snapshot.InstructionCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  support_shape: supported={snapshot.SupportedInstructionCount}, unsupported={snapshot.UnsupportedInstructionCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  owner_storage: encoded_bytes={snapshot.OwnerBackedEncodedBytes}, estimated_compact_encoded_bytes={snapshot.EstimatedCompactEncodedBytes}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  operand_tables: operand_entries={snapshot.OperandTableEntryCount}, extra_operand_entries={snapshot.ExtraOperandTableEntryCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  expression_refs: primary={snapshot.ExpressionReferenceCount}, secondary={snapshot.SecondaryExpressionReferenceCount}, table_count={snapshot.ExpressionProgramReferenceTableCount}"));
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  symbols_and_bindings: symbol_operands={snapshot.SymbolOperandCount}, binding_target_operands={snapshot.BindingTargetOperandCount}"));

    PrintTopHistogram("instruction_kind_histogram", snapshot.InstructionKindHistogram);
    PrintTopHistogram("supported_instruction_kind_histogram", snapshot.SupportedInstructionKindHistogram);
    PrintTopHistogram("unsupported_instruction_kind_histogram", snapshot.UnsupportedInstructionKindHistogram);
    PrintTopHistogram("unsupported_family_reason_histogram", snapshot.UnsupportedFamilyReasonHistogram);
}

void PrintTopHistogram<T>(string label, IReadOnlyList<KeyValuePair<T, long>> histogram)
{
    if (histogram.Count == 0)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {label}: (empty)"));
        return;
    }

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"  {label}:"));
    var count = Math.Min(10, histogram.Count);
    for (var i = 0; i < count; i++)
    {
        var (key, value) = histogram[i];
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    {key}: {value}"));
    }
}

JsEngine CreateEngine(bool traceRealm)
{
    if (!traceRealm)
    {
        return new JsEngine();
    }

    var logger = new ConsoleLogger("ProfileRunner");
    return new JsEngine(new JsEngineOptions { Logger = logger });
}

Engine CreateJintEngine()
{
    return new Engine(options => options.TimeoutInterval(TimeSpan.FromMinutes(5)));
}

bool TryParseEngineKind(string value, out EngineKind engineKind)
{
    if (string.Equals(value, "jint", StringComparison.OrdinalIgnoreCase))
    {
        engineKind = EngineKind.Jint;
        return true;
    }

    if (string.Equals(value, "asynkron", StringComparison.OrdinalIgnoreCase))
    {
        engineKind = EngineKind.Asynkron;
        return true;
    }

    engineKind = EngineKind.Asynkron;
    return false;
}

static ProfileManifest LoadManifest(string manifestPath)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = doc.RootElement;
    if (!root.TryGetProperty("profiles", out var profilesElement))
    {
        throw new InvalidOperationException($"Manifest missing profiles: {manifestPath}");
    }

    var profiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
    foreach (var profileProperty in profilesElement.EnumerateObject())
    {
        var profileElement = profileProperty.Value;
        var definition = new ProfileDefinition
        {
            Name = GetString(profileElement, "name", profileProperty.Name),
            Description = GetString(profileElement, "description", string.Empty),
            Script = GetString(profileElement, "script", string.Empty),
            Mode = GetString(profileElement, "mode", "sync"),
            Iterations = GetInt(profileElement, "iterations", 1),
            Warmup = GetInt(profileElement, "warmup", 1),
            ShowProgress = GetBool(profileElement, "show_progress", false),
            ShowTiming = GetBool(profileElement, "show_timing", false),
            FreshEnginePerIteration = GetBool(profileElement, "fresh_engine_per_iteration", false),
            Header = GetOptionalString(profileElement, "header"),
            TraceRealmEnv = GetOptionalString(profileElement, "trace_realm_env"),
            TraceRealmRuns = GetInt(profileElement, "trace_realm_runs", 0)
        };

        profiles[profileProperty.Name] = definition;
    }

    var scriptsDir = "profile-scripts";
    if (root.TryGetProperty("scripts_dir", out var scriptsDirElement))
    {
        scriptsDir = scriptsDirElement.GetString() ?? scriptsDir;
    }

    return new ProfileManifest
    {
        ScriptsDir = scriptsDir,
        Profiles = profiles
    };
}

static string GetString(JsonElement element, string propertyName, string fallback)
{
    return element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? fallback : fallback;
}

static string? GetOptionalString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
}

static int GetInt(JsonElement element, string propertyName, int fallback)
{
    return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
        ? result
        : fallback;
}

static bool GetBool(JsonElement element, string propertyName, bool fallback)
{
    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True
        ? true
        : element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.False
            ? false
            : fallback;
}

static void PrintProfiles(ProfileManifest manifest)
{
    foreach (var (key, profile) in manifest.Profiles.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key,-16} {profile.Name} - {profile.Description}"));
    }
}

static string FindRepoRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
        var manifestPath = Path.Combine(current, "tools", "profile-manifest.json");
        if (File.Exists(manifestPath))
        {
            return current;
        }

        var parent = Directory.GetParent(current);
        if (parent == null)
        {
            break;
        }

        current = parent.FullName;
    }

    throw new InvalidOperationException("Unable to locate profile-manifest.json.");
}

sealed class ProfileManifest
{
    public string ScriptsDir { get; init; } = "profile-scripts";

    public Dictionary<string, ProfileDefinition> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

sealed class ProfileDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Script { get; init; } = string.Empty;

    public string Mode { get; init; } = "sync";

    public int Iterations { get; init; } = 1;

    public int Warmup { get; init; } = 1;

    public bool ShowProgress { get; init; }

    public bool ShowTiming { get; init; }

    public bool FreshEnginePerIteration { get; init; }

    public string? Header { get; init; }

    public string? TraceRealmEnv { get; init; }

    public int TraceRealmRuns { get; init; }
}

enum EngineKind
{
    Asynkron,
    Jint
}

readonly record struct BytecodeProfileCase(
    string Name,
    ExpressionProgram Program,
    int Iterations,
    bool ExpectsNumericResult = true);

sealed class ConsoleLogger(string name) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{name}] {logLevel}: {message}"));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

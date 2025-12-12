using Asynkron.JsEngine.Benchmarks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// Configure benchmark runs
var config = DefaultConfig.Instance
    .WithOptions(ConfigOptions.DisableOptimizationsValidator)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumn(RankColumn.Arabic)
    .AddColumn(StatisticColumn.Median)
    .AddColumn(StatisticColumn.P95)
    .AddExporter(JsonExporter.Full)
    .AddLogger(ConsoleLogger.Default)
    .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));

// Check for command line arguments to select benchmark type
if (args.Length == 0)
{
    Console.WriteLine("""
        JsEngine Performance Benchmarks
        ================================

        Usage: dotnet run -c Release -- [benchmark-type]

        Available benchmark types:
          all        - Run all benchmarks (takes a long time)
          lexer      - Lexer (tokenization) benchmarks
          parser     - Parser (AST generation) benchmarks
          evaluator  - Evaluator (execution) benchmarks
          fastpaths  - Property/identifier access micro-benchmarks
          pipeline   - Full pipeline phase comparison
          operations - Specific operation micro-benchmarks
          overhead   - Evaluation overhead analysis (event loop, engine init, etc.)

        Category filters (use with 'operations'):
          --filter *Arithmetic*   - Arithmetic operations only
          --filter *Property*     - Property access operations only
          --filter *FunctionCall* - Function call operations only
          --filter *Loop*         - Loop operations only
          --filter *Object*       - Object operations only
          --filter *Array*        - Array operations only
          --filter *String*       - String operations only
          --filter *Comparison*   - Comparison operations only

        Examples:
          dotnet run -c Release -- lexer
          dotnet run -c Release -- operations --filter *Loop*
          dotnet run -c Release -- all

        Quick mode (fewer iterations, faster results):
          dotnet run -c Release -- lexer --job short

        """);
    return;
}

var benchmarkType = args[0].ToLowerInvariant();

// Add short job if requested (uses in-process toolchain to avoid rebuild timeouts)
if (args.Contains("--job") && args.Contains("short"))
{
    config = config.AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
}
else
{
    // Use in-process toolchain by default to avoid build timeouts
    config = config.AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
}

switch (benchmarkType)
{
    case "all":
        Console.WriteLine("Running ALL benchmarks. This will take a while...\n");
        BenchmarkRunner.Run<LexerBenchmarks>(config);
        BenchmarkRunner.Run<ParserBenchmarks>(config);
        BenchmarkRunner.Run<EvaluatorBenchmarks>(config);
        BenchmarkRunner.Run<PipelineBenchmarks>(config);
        BenchmarkRunner.Run<OperationBenchmarks>(config);
        break;

    case "lexer":
        Console.WriteLine("Running Lexer benchmarks...\n");
        BenchmarkRunner.Run<LexerBenchmarks>(config);
        break;

    case "parser":
        Console.WriteLine("Running Parser benchmarks...\n");
        BenchmarkRunner.Run<ParserBenchmarks>(config);
        break;

    case "evaluator":
        Console.WriteLine("Running Evaluator benchmarks...\n");
        BenchmarkRunner.Run<EvaluatorBenchmarks>(config);
        break;

    case "fastpaths":
        Console.WriteLine("Running property/identifier access benchmarks...\n");
        BenchmarkRunner.Run<FastPathBenchmarks>(config);
        break;

    case "pipeline":
        Console.WriteLine("Running Pipeline phase comparison benchmarks...\n");
        BenchmarkRunner.Run<PipelineBenchmarks>(config);
        break;

    case "operations":
        Console.WriteLine("Running Operation micro-benchmarks...\n");
        // Pass remaining args to allow filtering
        BenchmarkSwitcher.FromAssembly(typeof(OperationBenchmarks).Assembly)
            .Run(args.Skip(1).ToArray(), config);
        break;

    case "overhead":
        Console.WriteLine("Running Evaluation overhead benchmarks...\n");
        BenchmarkRunner.Run<EvaluationOverheadBenchmarks>(config);
        break;

    default:
        // Try to run with BenchmarkSwitcher for custom filters
        BenchmarkSwitcher.FromAssembly(typeof(LexerBenchmarks).Assembly).Run(args, config);
        break;
}

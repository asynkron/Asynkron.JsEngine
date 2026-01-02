using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Benchmarks to isolate evaluation overhead vs actual AST execution.
/// This helps identify if the event loop, engine initialization, or AST
/// evaluation itself is the bottleneck.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class EvaluationOverheadBenchmarks
{
    private JsEngine _engine = null!;
    private JsEngine _reusableEngine = null!;

    // Pre-parsed artifacts
    private IReadOnlyList<Token> _simpleTokens = null!;
    private ProgramNode _simpleAst = null!;
    private string _simpleSource = null!;

    private IReadOnlyList<Token> _loopTokens = null!;
    private ProgramNode _loopAst = null!;
    private string _loopSource = null!;

    [GlobalSetup]
    public void Setup()
    {
        _reusableEngine = new JsEngine();
        _reusableEngine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        // Simple expression - minimal work
        _simpleSource = "1 + 2";

        // Loop - actual computational work
        _loopSource = """
            var sum = 0;
            for (var i = 0; i < 10000; i++) {
                sum += i;
            }
            sum;
            """;

        // Pre-tokenize
        _simpleTokens = new JsLexer(_simpleSource).Tokenize();
        _loopTokens = new JsLexer(_loopSource).Tokenize();

        // Pre-parse
        _simpleAst = new JsAstParser(_simpleTokens, _simpleSource).ParseProgram();
        _loopAst = new JsAstParser(_loopTokens, _loopSource).ParseProgram();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _reusableEngine.DisposeAsync();
        if (_engine != null)
            await _engine.DisposeAsync();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _engine?.DisposeAsync().AsTask().Wait();
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);
    }

    // ==================== ENGINE CREATION OVERHEAD ====================

    /// <summary>
    /// Measures just creating a new JsEngine instance (with all stdlib registration).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Overhead")]
    public JsEngine EngineCreation()
    {
        var engine = new JsEngine();
        return engine;
    }

    /// <summary>
    /// Measures engine disposal overhead.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Overhead")]
    public async Task EngineDisposal()
    {
        var engine = new JsEngine();
        await engine.DisposeAsync();
    }

    // ==================== LEXING + PARSING ====================

    /// <summary>
    /// Just lexing a simple expression.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parsing")]
    public IReadOnlyList<Token> Simple_LexOnly()
    {
        return new JsLexer(_simpleSource).Tokenize();
    }

    /// <summary>
    /// Just parsing a simple expression (tokens pre-created).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parsing")]
    public ProgramNode Simple_ParseOnly()
    {
        return new JsAstParser(_simpleTokens, _simpleSource).ParseProgram();
    }

    /// <summary>
    /// Just lexing a loop.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parsing")]
    public IReadOnlyList<Token> Loop_LexOnly()
    {
        return new JsLexer(_loopSource).Tokenize();
    }

    /// <summary>
    /// Just parsing a loop (tokens pre-created).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Parsing")]
    public ProgramNode Loop_ParseOnly()
    {
        return new JsAstParser(_loopTokens, _loopSource).ParseProgram();
    }

    // ==================== FULL EVALUATION WITH NEW ENGINE ====================

    /// <summary>
    /// Full evaluation with fresh engine each time - includes all overhead.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FullEvaluation")]
    public async Task<object?> Simple_FullEvaluation_NewEngine()
    {
        return await _engine.Evaluate(_simpleSource);
    }

    /// <summary>
    /// Full evaluation of loop with fresh engine.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("FullEvaluation")]
    public async Task<object?> Loop_FullEvaluation_NewEngine()
    {
        return await _engine.Evaluate(_loopSource);
    }

    // ==================== EVALUATION WITH REUSED ENGINE ====================

    /// <summary>
    /// Evaluation reusing the same engine - should have less startup overhead.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ReusedEngine")]
    public async Task<object?> Simple_Evaluation_ReusedEngine()
    {
        return await _reusableEngine.Evaluate(_simpleSource);
    }

    /// <summary>
    /// Loop evaluation reusing the same engine.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("ReusedEngine")]
    public async Task<object?> Loop_Evaluation_ReusedEngine()
    {
        return await _reusableEngine.Evaluate(_loopSource);
    }

    // ==================== SYNCHRONOUS EVALUATION (NO EVENT LOOP) ====================

    /// <summary>
    /// Synchronous evaluation - bypasses the event loop entirely.
    /// This should be MUCH faster for simple synchronous code.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SyncEvaluation")]
    public object? Simple_EvaluateSync()
    {
        return _reusableEngine.EvaluateSync(_simpleSource);
    }

    /// <summary>
    /// Synchronous evaluation of loop - bypasses the event loop.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SyncEvaluation")]
    public object? Loop_EvaluateSync()
    {
        return _reusableEngine.EvaluateSync(_loopSource);
    }

    /// <summary>
    /// Multiple synchronous evaluations.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SyncEvaluation")]
    public object? Simple_10xEvaluateSync()
    {
        object? result = null;
        for (int i = 0; i < 10; i++)
        {
            result = _reusableEngine.EvaluateSync(_simpleSource);
        }
        return result;
    }

    /// <summary>
    /// Multiple synchronous loop evaluations.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("SyncEvaluation")]
    public object? Loop_10xEvaluateSync()
    {
        object? result = null;
        for (int i = 0; i < 10; i++)
        {
            result = _reusableEngine.EvaluateSync(_loopSource);
        }
        return result;
    }

    // ==================== MULTIPLE EVALUATIONS (AMORTIZED OVERHEAD) ====================

    /// <summary>
    /// Multiple simple evaluations on the same engine.
    /// This shows the cost when event loop is already running.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Amortized")]
    public async Task<object?> Simple_10xEvaluations_ReusedEngine()
    {
        object? result = null;
        for (int i = 0; i < 10; i++)
        {
            result = await _reusableEngine.Evaluate(_simpleSource);
        }
        return result;
    }

    /// <summary>
    /// Multiple loop evaluations on the same engine.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Amortized")]
    public async Task<object?> Loop_10xEvaluations_ReusedEngine()
    {
        object? result = null;
        for (int i = 0; i < 10; i++)
        {
            result = await _reusableEngine.Evaluate(_loopSource);
        }
        return result;
    }

    // ==================== DIRECT AST EVALUATION (BYPASSING EVENT LOOP) ====================

    /// <summary>
    /// Direct AST evaluation bypassing all event loop machinery.
    /// Uses reflection to access internal APIs.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DirectEval")]
    public object? Simple_DirectAstEvaluation()
    {
        // Use the engine's Parse method which does lex + parse + transformations
        var program = _reusableEngine.Parse(_simpleSource);

        // Get internal members via reflection
        var engineType = typeof(JsEngine);
        var globalEnvProp = engineType.GetProperty("GlobalEnvironment",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var realmStateProp = engineType.GetProperty("RealmState",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var globalEnv = (JsEnvironment)globalEnvProp!.GetValue(_reusableEngine)!;
        var realmState = (RealmState)realmStateProp!.GetValue(_reusableEngine)!;

        // Directly evaluate the AST
        return program.EvaluateProgram(globalEnv, realmState);
    }

    /// <summary>
    /// Direct AST evaluation of loop bypassing all event loop machinery.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("DirectEval")]
    public object? Loop_DirectAstEvaluation()
    {
        var program = _reusableEngine.Parse(_loopSource);

        var engineType = typeof(JsEngine);
        var globalEnvProp = engineType.GetProperty("GlobalEnvironment",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var realmStateProp = engineType.GetProperty("RealmState",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var globalEnv = (JsEnvironment)globalEnvProp!.GetValue(_reusableEngine)!;
        var realmState = (RealmState)realmStateProp!.GetValue(_reusableEngine)!;

        return program.EvaluateProgram(globalEnv, realmState);
    }
}

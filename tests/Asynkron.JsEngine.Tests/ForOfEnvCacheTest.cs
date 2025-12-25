using System;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ForOfEnvCacheTest
{
    private readonly ITestOutputHelper _output;

    public ForOfEnvCacheTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 10000)]
    public async Task ForOf_NestedInLoop_ShouldReuseIterationEnvironment()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Warm up the engine
        await engine.Evaluate("1+1");
        var warmupMessages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Run a for-of loop nested inside a regular for loop
        // The optimization should cache the iteration environment at the AST level
        // so we only allocate 1 iteration environment, not 100
        var script = """
            (function() {
                let sum = 0;
                const arr = [1, 2, 3, 4, 5];
                for (let i = 0; i < 100; i++) {
                    for (const n of arr) {
                        sum += n;
                    }
                }
                return sum;
            })();
        """;

        // Parse ONCE and evaluate the parsed program
        // (Calling ParseProgram and then Evaluate would parse TWICE with different ScopeIds
        // because ScopeAnalyzer is shared per engine instance)
        var parsed = engine.ParseProgram(script);
        PrintAstInfo(parsed);

        var result = await engine.Evaluate(parsed);

        Assert.Equal(1500d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));

        // Get all messages after warmup
        var allMessages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        var postWarmupMessages = allMessages.Skip(warmupMessages.Length).ToArray();

        // Print relevant messages for debugging
        foreach (var msg in postWarmupMessages.Where(m =>
            m.Contains("ForOf optimization", StringComparison.Ordinal) ||
            m.Contains("for-each-iteration", StringComparison.Ordinal)))
        {
            _output.WriteLine(msg);
        }

        // NEGATIVE ASSERTION: We should NOT see "ForOf optimization NOT used" for our simple pattern
        var notUsedMessages = postWarmupMessages
            .Where(m => m.Contains("ForOf optimization NOT used", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(notUsedMessages);

        // POSITIVE ASSERTION: We SHOULD see "Using cached iteration environment"
        var usingCachedMessages = postWarmupMessages
            .Count(m => m.Contains("Using cached iteration environment", StringComparison.Ordinal));

        // We should see at least 1 "using cached" message (ideally exactly 1 per outer loop entry, but
        // since we're checking the log contains it, we just need to confirm it's used)
        Assert.True(usingCachedMessages >= 1,
            $"Expected at least 1 'Using cached iteration environment' log, got {usingCachedMessages}");

        // Count for-each-iteration allocations (should be small due to caching)
        var forEachIterationAllocations = postWarmupMessages
            .Count(m => m.Contains("for-each-iteration", StringComparison.Ordinal));

        // With the optimization, we should have at most a small number of for-each-iteration allocations
        // (ideally just 1, not 100)
        Assert.True(forEachIterationAllocations <= 5,
            $"Expected at most 5 for-each-iteration allocations, got {forEachIterationAllocations}");
    }

    private void PrintAstInfo(ProgramNode program)
    {
        void PrintNode(AstNode node, int indent)
        {
            var prefix = new string(' ', indent * 2);
            switch (node)
            {
                case FunctionExpression fe:
                    _output.WriteLine($"{prefix}FunctionExpression: ScopeId={fe.ScopeId}, SlotCount={fe.SlotCount}");
                    PrintNode(fe.Body, indent + 1);
                    break;
                case BlockStatement bs:
                    _output.WriteLine($"{prefix}BlockStatement: ScopeId={bs.ScopeId}, SlotCount={bs.SlotCount}");
                    foreach (var stmt in bs.Statements) PrintNode(stmt, indent + 1);
                    break;
                case ForStatement fs:
                    _output.WriteLine($"{prefix}ForStatement: PerIterationScopeId={fs.PerIterationScopeId}, LoopEnvSlotIndex={fs.LoopEnvSlotIndex}, LoopEnvScopeId={fs.LoopEnvScopeId}");
                    PrintNode(fs.Body, indent + 1);
                    break;
                case ForEachStatement fes:
                    _output.WriteLine($"{prefix}ForEachStatement: PerIterationScopeId={fes.PerIterationScopeId}, LoopEnvSlotIndex={fes.LoopEnvSlotIndex}, LoopEnvScopeId={fes.LoopEnvScopeId}");
                    PrintNode(fes.Body, indent + 1);
                    break;
                case ExpressionStatement es:
                    PrintNode(es.Expression, indent);
                    break;
                case CallExpression ce:
                    PrintNode(ce.Callee, indent);
                    break;
                default:
                    _output.WriteLine($"{prefix}{node.GetType().Name}");
                    break;
            }
        }

        _output.WriteLine("=== AST INFO ===");
        foreach (var stmt in program.Body)
        {
            PrintNode(stmt, 0);
        }
        _output.WriteLine("================");
    }
}

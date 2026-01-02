using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for closures accessing block-scoped variables.
/// This is the exact failing pattern from Test262 block-scope/shadowing/lookup-from-closure.js.
/// </summary>
public sealed class BlockScopeClosureTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task NestedFunctionInBlock_CanAccessBlockScopedVariables()
    {
        // This is the exact Test262 failure case
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function f5(one) {
              var x = one + 1;
              let y = one + 2;
              const u = one + 4;
              {
                let z = one + 3;
                const v = one + 5;
                function f() {
                  return [one, x, y, z, u, v];
                }
                return f();
              }
            }
            f5(1);
            """);

        Output.WriteLine($"Result: {result}");
        Output.WriteLine($"Result Type: {result?.GetType().Name}");

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1.0, array.GetElement(0).AsDouble()); // one = 1
        Assert.Equal(2.0, array.GetElement(1).AsDouble()); // x = one + 1 = 2
        Assert.Equal(3.0, array.GetElement(2).AsDouble()); // y = one + 2 = 3
        Assert.Equal(4.0, array.GetElement(3).AsDouble()); // z = one + 3 = 4
        Assert.Equal(5.0, array.GetElement(4).AsDouble()); // u = one + 4 = 5
        Assert.Equal(6.0, array.GetElement(5).AsDouble()); // v = one + 5 = 6
    }

    [Fact]
    public async Task SimpleBlockShadowing_InnerBlockOverridesOuter()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function fn(a) {
                let b = 1;
                {
                    let a = 2;
                    let b = 2;
                    return [a, b];
                }
            }
            fn(1);
            """);

        Output.WriteLine($"Result: {result}");

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(2.0, array.GetElement(0).AsDouble()); // inner a = 2
        Assert.Equal(2.0, array.GetElement(1).AsDouble()); // inner b = 2
    }

    [Fact]
    public async Task ClosureInBlock_AccessesBlockVariable()
    {
        await using var engine = CreateEngine();

        // First parse and inspect the AST
        var program = engine.ParseProgram("""
            function outer() {
                {
                    let z = 42;
                    function inner() {
                        return z;
                    }
                    return inner();
                }
            }
            outer();
            """);

        // Debug output
        var outerDecl = program.Body[0] as FunctionDeclaration;
        Output.WriteLine($"outer function found: {outerDecl != null}");
        if (outerDecl != null)
        {
            var cache = ((IAstCacheable<ExecutionPlanCache>)outerDecl.Function).GetOrCreateCache();
            Output.WriteLine($"outer plan built: {cache.Succeeded}");

            // Check the instructions in the plan
            if (cache.Succeeded && cache.Plan != null)
            {
                Output.WriteLine($"outer plan has {cache.Plan.Instructions.Length} instructions");
                foreach (var instr in cache.Plan.Instructions)
                {
                    Output.WriteLine($"  Instr: {instr.GetType().Name}");
                    if (instr is StatementInstruction stmtInstr)
                    {
                        Output.WriteLine($"    Statement: {stmtInstr.Statement.GetType().Name}");
                        if (stmtInstr.Statement is BlockStatement blk)
                        {
                            Output.WriteLine($"    Block ScopeId: {blk.ScopeId}, SlotCount: {blk.SlotCount}");
                            Output.WriteLine($"    Block SlotMap keys: {string.Join(", ", blk.SlotMap.Keys.Select(k => k.Name))}");

                            // Check the inner function declaration in the stamped block
                            foreach (var stmtInBlk in blk.Statements)
                            {
                                if (stmtInBlk is FunctionDeclaration stampedInnerDecl)
                                {
                                    Output.WriteLine($"    Stamped block has inner function: {stampedInnerDecl.Name.Name}");
                                    Output.WriteLine($"    Stamped FunctionExpression hash: {stampedInnerDecl.Function.GetHashCode()}");
                                    var stampedInnerCache = ((IAstCacheable<ExecutionPlanCache>)stampedInnerDecl.Function).GetOrCreateCache();
                                    Output.WriteLine($"    Stamped inner plan built: {stampedInnerCache.Succeeded}");
                                    Output.WriteLine($"    Stamped inner plan hash: {stampedInnerCache.Plan?.GetHashCode()}");

                                    if (stampedInnerCache.Succeeded && stampedInnerCache.Plan != null)
                                    {
                                        Output.WriteLine($"    Stamped inner plan has {stampedInnerCache.Plan.Instructions.Length} instructions");
                                        foreach (var innerInstr in stampedInnerCache.Plan.Instructions)
                                        {
                                            Output.WriteLine($"      Inner instr: {innerInstr.GetType().Name}");
                                            if (innerInstr is ReturnInstruction retInstr && retInstr.ReturnExpression is IdentifierExpression retId)
                                            {
                                                Output.WriteLine($"        Return z - ScopeId: {retId.ScopeId}, SlotIndex: {retId.SlotIndex}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var block = outerDecl.Function.Body.Statements[0] as BlockStatement;
            Output.WriteLine($"AST block found: {block != null}");
            if (block != null)
            {
                Output.WriteLine($"AST block ScopeId: {block.ScopeId}, SlotCount: {block.SlotCount}");

                foreach (var stmt in block.Statements)
                {
                    if (stmt is FunctionDeclaration innerDecl)
                    {
                        Output.WriteLine($"inner function found: {innerDecl.Name.Name}");
                        Output.WriteLine($"AST FunctionExpression hash: {innerDecl.Function.GetHashCode()}");
                        var innerCache = ((IAstCacheable<ExecutionPlanCache>)innerDecl.Function).GetOrCreateCache();
                        Output.WriteLine($"inner plan built: {innerCache.Succeeded}");
                        Output.WriteLine($"AST inner plan hash: {innerCache.Plan?.GetHashCode()}");

                        // Check the return statement's identifier
                        var returnStmt = innerDecl.Function.Body.Statements[0] as ReturnStatement;
                        if (returnStmt?.Expression is IdentifierExpression id)
                        {
                            Output.WriteLine($"return z (AST) - ScopeId: {id.ScopeId}, SlotIndex: {id.SlotIndex}");
                        }
                    }
                }
            }
        }

        object? result = null;
        try
        {
            result = await engine.Evaluate(program);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Exception: {ex.Message}");
        }
        finally
        {
            // Dump runtime logs
            Output.WriteLine("\n=== RUNTIME LOGS ===");
            foreach (var log in CurrentLogger?.Collector.Snapshot() ?? [])
            {
                // Show all relevant logs including the missing 'z' lookup
                if (log.Message.Contains("EvaluateBlockSlowCore") ||
                    log.Message.Contains("InstantiateLexicalBlockFunctions") ||
                    log.Message.Contains("Identifier slot") ||
                    log.Message.Contains("name=z") ||
                    log.Message.Contains("inner") ||
                    log.Message.Contains("Lookup") ||
                    log.Message.Contains("TraceLookup") ||
                    log.Message.Contains("SyncFunctionInvoker") ||
                    log.Message.Contains("TryResolve") ||
                    log.Message.Contains("Invoke.ALL"))
                {
                    Output.WriteLine(log.Message);
                }
            }
        }

        Output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, (double)result!);
    }

    [Fact]
    public async Task ClosureInFunctionScope_WorksWithoutBlock()
    {
        // Simpler case - no block scope, just function scope
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function outer() {
                let z = 42;
                function inner() {
                    return z;
                }
                return inner();
            }
            outer();
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, (double)result!);
    }

    [Fact]
    public async Task BlockShadowing_WithoutClosure()
    {
        // Test that block shadowing works without closures
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            let x = 1;
            {
                let x = 2;
                x;
            }
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, (double)result!);
    }
}

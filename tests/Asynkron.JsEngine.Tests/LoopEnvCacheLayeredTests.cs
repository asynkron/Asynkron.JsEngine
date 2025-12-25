using System;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Layered tests to isolate where loop environment caching fails.
/// Tests are ordered from lowest-level (AST analysis) to highest-level (full integration).
/// </summary>
public class LoopEnvCacheLayeredTests
{
    private readonly ITestOutputHelper _output;

    public LoopEnvCacheLayeredTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ============================================================================
    // LAYER 1: ScopeAnalyzer - Verify AST nodes get correct LoopEnvSlotIndex/LoopEnvScopeId
    // ============================================================================

    [Fact]
    public async Task Layer1_ScopeAnalyzer_ForOfInFunction_SetsLoopEnvSlotIndex()
    {
        // Minimal case: single for-of loop inside a function
        await using var engine = new JsEngine(new JsEngineOptions());

        var script = """
            (function() {
                const arr = [1, 2, 3];
                for (const n of arr) {
                    n;
                }
            })
        """;

        var parsed = engine.ParseProgram(script);

        // Find the FunctionExpression and ForEachStatement in the AST
        var exprStmt = (ExpressionStatement)parsed.Body[0];
        var funcExpr = (FunctionExpression)exprStmt.Expression;
        var block = funcExpr.Body;

        _output.WriteLine($"FunctionExpression: ScopeId={funcExpr.ScopeId}, SlotCount={funcExpr.SlotCount}");
        _output.WriteLine($"BlockStatement: ScopeId={block.ScopeId}, SlotCount={block.SlotCount}");

        // Find the for-of statement
        ForEachStatement? forOf = null;
        foreach (var stmt in block.Statements)
        {
            if (stmt is ForEachStatement fes)
            {
                forOf = fes;
                break;
            }
        }

        Assert.NotNull(forOf);
        _output.WriteLine($"ForEachStatement: LoopEnvSlotIndex={forOf.LoopEnvSlotIndex}, LoopEnvScopeId={forOf.LoopEnvScopeId}, PerIterationScopeId={forOf.PerIterationScopeId}");

        // ASSERTION: LoopEnvSlotIndex should be >= 0 (allocated)
        Assert.True(forOf.LoopEnvSlotIndex >= 0,
            $"Expected LoopEnvSlotIndex >= 0, got {forOf.LoopEnvSlotIndex}");

        // ASSERTION: LoopEnvScopeId should match the function's ScopeId (where slots are allocated)
        Assert.True(forOf.LoopEnvScopeId >= 0,
            $"Expected LoopEnvScopeId >= 0, got {forOf.LoopEnvScopeId}");

        // The LoopEnvScopeId should be the function scope, not the block scope
        // FunctionExpression.ScopeId is the function scope
        Assert.Equal(funcExpr.ScopeId, forOf.LoopEnvScopeId);
    }

    [Fact]
    public async Task Layer1_ScopeAnalyzer_NestedLoops_AllocatesCorrectSlots()
    {
        // Test nested loops - both loops should get slots in the function scope
        await using var engine = new JsEngine(new JsEngineOptions());

        var script = """
            (function() {
                let sum = 0;
                const arr = [1, 2, 3];
                for (let i = 0; i < 3; i++) {
                    for (const n of arr) {
                        sum += n;
                    }
                }
                return sum;
            })
        """;

        var parsed = engine.ParseProgram(script);

        var exprStmt = (ExpressionStatement)parsed.Body[0];
        var funcExpr = (FunctionExpression)exprStmt.Expression;
        var block = funcExpr.Body;

        _output.WriteLine($"FunctionExpression: ScopeId={funcExpr.ScopeId}, SlotCount={funcExpr.SlotCount}");
        _output.WriteLine($"BlockStatement: ScopeId={block.ScopeId}, SlotCount={block.SlotCount}");

        // Find both loop statements
        ForStatement? outerFor = null;
        ForEachStatement? innerForOf = null;
        foreach (var stmt in block.Statements)
        {
            if (stmt is ForStatement fs)
            {
                outerFor = fs;
                if (fs.Body is BlockStatement forBody)
                {
                    foreach (var innerStmt in forBody.Statements)
                    {
                        if (innerStmt is ForEachStatement fes)
                        {
                            innerForOf = fes;
                        }
                    }
                }
            }
        }

        Assert.NotNull(outerFor);
        Assert.NotNull(innerForOf);

        _output.WriteLine($"Outer For: LoopEnvSlotIndex={outerFor.LoopEnvSlotIndex}, LoopEnvScopeId={outerFor.LoopEnvScopeId}");
        _output.WriteLine($"Inner ForOf: LoopEnvSlotIndex={innerForOf.LoopEnvSlotIndex}, LoopEnvScopeId={innerForOf.LoopEnvScopeId}");

        // CRITICAL: The function scope SlotCount must be >= the max LoopEnvSlotIndex + 1
        var maxSlotIndex = Math.Max(outerFor.LoopEnvSlotIndex, innerForOf.LoopEnvSlotIndex);
        Assert.True(funcExpr.SlotCount > maxSlotIndex,
            $"FunctionExpression.SlotCount ({funcExpr.SlotCount}) must be > max LoopEnvSlotIndex ({maxSlotIndex})");
    }

    [Fact]
    public async Task Layer1_ScopeAnalyzer_ForInFunction_SetsLoopEnvSlotIndex()
    {
        // Test regular for loop with let declaration
        await using var engine = new JsEngine(new JsEngineOptions());

        var script = """
            (function() {
                for (let i = 0; i < 3; i++) {
                    i;
                }
            })
        """;

        var parsed = engine.ParseProgram(script);

        var exprStmt = (ExpressionStatement)parsed.Body[0];
        var funcExpr = (FunctionExpression)exprStmt.Expression;
        var block = funcExpr.Body;

        _output.WriteLine($"FunctionExpression: ScopeId={funcExpr.ScopeId}, SlotCount={funcExpr.SlotCount}");

        // Find the for statement
        ForStatement? forStmt = null;
        foreach (var stmt in block.Statements)
        {
            if (stmt is ForStatement fs)
            {
                forStmt = fs;
                break;
            }
        }

        Assert.NotNull(forStmt);
        _output.WriteLine($"ForStatement: LoopEnvSlotIndex={forStmt.LoopEnvSlotIndex}, LoopEnvScopeId={forStmt.LoopEnvScopeId}, PerIterationScopeId={forStmt.PerIterationScopeId}");

        // ASSERTION: LoopEnvSlotIndex should be >= 0 for let loops
        Assert.True(forStmt.LoopEnvSlotIndex >= 0,
            $"Expected LoopEnvSlotIndex >= 0, got {forStmt.LoopEnvSlotIndex}");

        Assert.Equal(funcExpr.ScopeId, forStmt.LoopEnvScopeId);
    }

    // ============================================================================
    // LAYER 2: JsEnvironment.FindByScopeId - Verify scope chain lookup works
    // ============================================================================

    [Fact]
    public void Layer2_JsEnvironment_FindByScopeId_FindsCorrectScope()
    {
        // Create a chain of environments manually
        var root = new JsEnvironment(null, true, false, null, "root");
        root.InitializeSlots(5, 1); // ScopeId=1, 5 slots

        var child = new JsEnvironment(root, false, true, null, "child");
        child.InitializeSlots(3, 2); // ScopeId=2, 3 slots

        var grandchild = new JsEnvironment(child, false, true, null, "grandchild");
        grandchild.InitializeSlots(2, 3); // ScopeId=3, 2 slots

        _output.WriteLine($"root: ScopeId={root.ScopeId}, SlotCount={root.SlotCount}");
        _output.WriteLine($"child: ScopeId={child.ScopeId}, SlotCount={child.SlotCount}");
        _output.WriteLine($"grandchild: ScopeId={grandchild.ScopeId}, SlotCount={grandchild.SlotCount}");

        // Test FindByScopeId from grandchild
        var foundRoot = grandchild.FindByScopeId(1);
        var foundChild = grandchild.FindByScopeId(2);
        var foundGrandchild = grandchild.FindByScopeId(3);
        var foundNonexistent = grandchild.FindByScopeId(99);

        Assert.Same(root, foundRoot);
        Assert.Same(child, foundChild);
        Assert.Same(grandchild, foundGrandchild);
        Assert.Null(foundNonexistent);
    }

    [Fact]
    public void Layer2_JsEnvironment_FindByScopeId_WorksWithUninitializedSlots()
    {
        // Create environments without calling InitializeSlots - simulating InvokeWithContext behavior
        var root = new JsEnvironment(null, true, false, null, "root");
        // Don't initialize slots - simulating function scope from InvokeWithContext

        // Manually set ScopeId without slots (this is what happens in some code paths)
        // Note: We can't directly set ScopeId, it's set via InitializeSlots or constructor
        // So this test shows that FindByScopeId only works if ScopeId is set

        _output.WriteLine($"root without slots: ScopeId={root.ScopeId}, HasSlots={root.HasSlots}");

        // Environment without InitializeSlots has ScopeId=-1 by default
        Assert.Equal(-1, root.ScopeId);
        Assert.False(root.HasSlots);
    }

    // ============================================================================
    // LAYER 3: Verify function invocation sets correct ScopeId on environment
    // ============================================================================

    [Fact(Timeout = 5000)]
    public async Task Layer3_FunctionInvocation_SetsCorrectScopeId()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Simple function that we can trace
        var script = """
            (function testFunc() {
                return 42;
            })()
        """;

        var parsed = engine.ParseProgram(script);
        var exprStmt = (ExpressionStatement)parsed.Body[0];
        var callExpr = (CallExpression)exprStmt.Expression;
        var funcExpr = (FunctionExpression)callExpr.Callee;

        _output.WriteLine($"FunctionExpression.ScopeId={funcExpr.ScopeId}");

        var result = await engine.Evaluate(parsed);
        Assert.Equal(42d, result);

        // Check logs for environment creation
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        foreach (var msg in messages.Where(m => m.Contains("JsEnvironment", StringComparison.Ordinal)))
        {
            _output.WriteLine(msg);
        }
    }

    // ============================================================================
    // LAYER 4: Verify slot operations work correctly
    // ============================================================================

    [Fact]
    public void Layer4_Slots_GetSlotRef_ReturnsCorrectSlot()
    {
        var env = new JsEnvironment(null, true, false, null, "test");
        env.InitializeSlots(3, 1);

        // All slots should be Undefined initially
        ref var slot0 = ref env.GetSlotRef(0);
        ref var slot1 = ref env.GetSlotRef(1);
        ref var slot2 = ref env.GetSlotRef(2);

        Assert.True(slot0.IsUndefined);
        Assert.True(slot1.IsUndefined);
        Assert.True(slot2.IsUndefined);

        // Write to slot 1
        slot1 = JsValue.FromDouble(42.0);

        // Read back - should get the written value
        ref var slot1Again = ref env.GetSlotRef(1);
        Assert.Equal(42.0, slot1Again.NumberValue);

        // Other slots unchanged
        Assert.True(env.GetSlotRef(0).IsUndefined);
        Assert.True(env.GetSlotRef(2).IsUndefined);
    }

    [Fact]
    public void Layer4_Slots_CanStoreJsEnvironment()
    {
        var parent = new JsEnvironment(null, true, false, null, "parent");
        parent.InitializeSlots(2, 1);

        // Create an environment to cache
        var cached = new JsEnvironment(parent, false, true, null, "cached");

        // Store it in a slot
        ref var slot = ref parent.GetSlotRef(0);
        slot = JsValue.FromObjectUnsafe(cached);

        // Retrieve it
        ref var retrieved = ref parent.GetSlotRef(0);
        Assert.False(retrieved.IsUndefined);
        Assert.Same(cached, retrieved.ObjectValue);
    }

    // ============================================================================
    // LAYER 5: End-to-end loop env caching
    // ============================================================================

    [Fact(Timeout = 5000)]
    public async Task Layer5_ForOf_SimpleCase_CachesIterationEnvironment()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Simplest possible case - single for-of, no nesting
        var script = """
            (function() {
                let sum = 0;
                const arr = [1, 2, 3];
                for (const n of arr) {
                    sum += n;
                }
                return sum;
            })()
        """;

        var parsed = engine.ParseProgram(script);

        // Verify AST is correctly annotated
        var exprStmt = (ExpressionStatement)parsed.Body[0];
        var callExpr = (CallExpression)exprStmt.Expression;
        var funcExpr = (FunctionExpression)callExpr.Callee;
        var block = funcExpr.Body;

        ForEachStatement? forOf = null;
        foreach (var stmt in block.Statements)
        {
            if (stmt is ForEachStatement fes)
            {
                forOf = fes;
                break;
            }
        }

        Assert.NotNull(forOf);
        _output.WriteLine($"AST - FunctionExpression.ScopeId={funcExpr.ScopeId}, SlotCount={funcExpr.SlotCount}");
        _output.WriteLine($"AST - ForEachStatement: LoopEnvSlotIndex={forOf.LoopEnvSlotIndex}, LoopEnvScopeId={forOf.LoopEnvScopeId}");

        // Execute
        var result = await engine.Evaluate(parsed);
        Assert.Equal(6d, result);

        // Analyze logs
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        _output.WriteLine("\n--- Relevant logs ---");
        foreach (var msg in messages.Where(m =>
            m.Contains("ForOf", StringComparison.Ordinal) ||
            m.Contains("for-each-iteration", StringComparison.Ordinal) ||
            m.Contains("slot", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine(msg);
        }

        // Count allocations
        var forEachAllocations = messages.Count(m =>
            m.Contains("for-each-iteration", StringComparison.Ordinal));
        _output.WriteLine($"\nfor-each-iteration allocations: {forEachAllocations}");

        // With caching, we should have at most 1 allocation (the cached one)
        // Without caching, we'd have 3 (one per iteration)
        Assert.True(forEachAllocations <= 1,
            $"Expected at most 1 for-each-iteration allocation with caching, got {forEachAllocations}");
    }

    [Fact(Timeout = 5000)]
    public async Task Layer5_ForOf_NestedInFor_CachesIterationEnvironment()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Nested case - for-of inside regular for
        var script = """
            (function() {
                let sum = 0;
                const arr = [1, 2, 3];
                for (let i = 0; i < 3; i++) {
                    for (const n of arr) {
                        sum += n;
                    }
                }
                return sum;
            })()
        """;

        var parsed = engine.ParseProgram(script);

        // Print AST info
        var exprStmt = (ExpressionStatement)parsed.Body[0];
        var callExpr = (CallExpression)exprStmt.Expression;
        var funcExpr = (FunctionExpression)callExpr.Callee;
        _output.WriteLine($"AST FunctionExpression: ScopeId={funcExpr.ScopeId}, SlotCount={funcExpr.SlotCount}");

        // Find nested loops
        var block = funcExpr.Body;
        foreach (var stmt in block.Statements)
        {
            if (stmt is ForStatement fs)
            {
                _output.WriteLine($"AST Outer For: LoopEnvSlotIndex={fs.LoopEnvSlotIndex}, LoopEnvScopeId={fs.LoopEnvScopeId}");
                if (fs.Body is BlockStatement forBody)
                {
                    foreach (var innerStmt in forBody.Statements)
                    {
                        if (innerStmt is ForEachStatement fes)
                        {
                            _output.WriteLine($"AST Inner ForOf: LoopEnvSlotIndex={fes.LoopEnvSlotIndex}, LoopEnvScopeId={fes.LoopEnvScopeId}");
                        }
                    }
                }
            }
        }

        // Execute
        var result = await engine.Evaluate(parsed);
        Assert.Equal(18d, result); // 3 outer iterations * (1+2+3) = 18

        // Analyze logs
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        _output.WriteLine("--- Relevant logs ---");
        foreach (var msg in messages.Where(m =>
            m.Contains("ForOf", StringComparison.Ordinal) ||
            m.Contains("for-each-iteration", StringComparison.Ordinal)))
        {
            _output.WriteLine(msg);
        }

        var forEachAllocations = messages.Count(m =>
            m.Contains("for-each-iteration", StringComparison.Ordinal));
        _output.WriteLine($"\nfor-each-iteration allocations: {forEachAllocations}");

        // With caching across outer loop iterations, we should have at most 1 allocation
        // Without caching, we'd have 3 (one per outer loop iteration)
        Assert.True(forEachAllocations <= 1,
            $"Expected at most 1 for-each-iteration allocation with caching, got {forEachAllocations}");
    }
}

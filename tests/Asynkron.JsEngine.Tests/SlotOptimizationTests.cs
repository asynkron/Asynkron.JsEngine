using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// SLOT OPTIMIZATION TESTS: Define the desired behavior for identifier slot assignment.
/// These tests currently FAIL because user variable identifiers have SlotIndex=-1.
/// Once the slot optimization is implemented, all tests will PASS.
///
/// See todo.md and docs/identifier-slot-optimization.md for the fix plan.
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Category(TestCategories.SlotOptimization)]
[Trait("Category", "SlotOptimization")]
public sealed class SlotOptimizationTests : IAsyncLifetime
{
    private JsEngine _engine = null!;

    public Task InitializeAsync()
    {
        _engine = new JsEngine();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _engine.DisposeAsync();
    }

    /// <summary>
    /// Loop variable 'i' in condition (i &lt; 10) should have slot info for fast lookup.
    /// </summary>
    [Fact]
    public async Task LoopVariable_InCondition_ShouldHaveSlotInfo()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                for (let i = 0; i < 10; i++) {
                    // body
                }
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()"); // Execute to trigger plan building

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var branchInstr = cache.Plan.Instructions
            .OfType<BranchInstruction>()
            .FirstOrDefault();
        Assert.NotNull(branchInstr);

        var leftId = GetFirstLoadIdentifier(branchInstr.ConditionProgram, "i");
        Assert.Equal("i", leftId.Name.Name);

        Assert.True(leftId.SlotIndex >= 0, $"Loop variable 'i' should have SlotIndex >= 0, but was {leftId.SlotIndex}");
        Assert.True(leftId.ScopeId >= 0, $"Loop variable 'i' should have ScopeId >= 0, but was {leftId.ScopeId}");
    }

    /// <summary>
    /// RHS identifier 'i' in compound assignment (s += i) should have slot info.
    /// </summary>
    [Fact]
    public async Task CompoundAssignment_RhsIdentifier_ShouldHaveSlotInfo()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let s = 0;
                for (let i = 0; i < 10; i++) {
                    s += i;
                }
                return s;
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()"); // Execute to trigger plan building

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var compoundInstr = cache.Plan.Instructions
            .OfType<CompoundAssignmentSlotInstruction>()
            .FirstOrDefault();
        Assert.NotNull(compoundInstr);
        Assert.Equal("s", compoundInstr.TargetSymbol.Name);

        if (compoundInstr.RhsExpression is IdentifierExpression rhsId)
        {
            Assert.Equal("i", rhsId.Name.Name);
            Assert.True(rhsId.SlotIndex >= 0, $"RHS 'i' should have SlotIndex >= 0, but was {rhsId.SlotIndex}");
            Assert.True(rhsId.ScopeId >= 0, $"RHS 'i' should have ScopeId >= 0, but was {rhsId.ScopeId}");
            return;
        }

        Assert.True(compoundInstr.RhsExpressionOps.HasValue, "Expected lowered RHS expression ops for compound assignment.");
        var rhsLoad = compoundInstr.RhsExpressionOps.Value
            .OfType<LoadIdentifierExpressionOp>()
            .FirstOrDefault(op => op.Name.Name == "i");
        Assert.NotNull(rhsLoad);
        Assert.True(rhsLoad.SlotIndex >= 0, $"RHS 'i' should have SlotIndex >= 0, but was {rhsLoad.SlotIndex}");
        Assert.True(rhsLoad.ScopeId >= 0, $"RHS 'i' should have ScopeId >= 0, but was {rhsLoad.ScopeId}");
    }

    /// <summary>
    /// Return variable 's' should have slot info for fast lookup.
    /// </summary>
    [Fact]
    public async Task ReturnStatement_Identifier_ShouldHaveSlotInfo()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let s = 0;
                for (let i = 0; i < 10; i++) {
                    s += i;
                }
                return s;
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()"); // Execute to trigger plan building

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var returnInstr = cache.Plan.Instructions
            .OfType<ReturnInstruction>()
            .FirstOrDefault(r => r.ReturnProgram is not null);
        Assert.NotNull(returnInstr);

        var returnId = GetFirstLoadIdentifier(returnInstr.ReturnProgram, "s");
        Assert.Equal("s", returnId.Name.Name);

        Assert.True(returnId.SlotIndex >= 0, $"Return variable 's' should have SlotIndex >= 0, but was {returnId.SlotIndex}");
        Assert.True(returnId.ScopeId >= 0, $"Return variable 's' should have ScopeId >= 0, but was {returnId.ScopeId}");
    }

    /// <summary>
    /// Identifiers should reference slots from PushEnvironmentInstruction.
    /// The environment may have slot info, but identifiers must also be stamped.
    /// </summary>
    [Fact]
    public async Task LoopEnvironment_Identifiers_ShouldReferenceSlots()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                for (let i = 0; i < 10; i++) {
                    // body
                }
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()"); // Execute to trigger plan building

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var branchInstr = cache.Plan.Instructions
            .OfType<BranchInstruction>()
            .FirstOrDefault();
        Assert.NotNull(branchInstr);

        var leftId = GetFirstLoadIdentifier(branchInstr.ConditionProgram, "i");

        Assert.True(leftId.SlotIndex >= 0, $"Loop variable 'i' should have SlotIndex >= 0, but was {leftId.SlotIndex}");
        Assert.True(leftId.ScopeId >= 0, $"Loop variable 'i' should have ScopeId >= 0, but was {leftId.ScopeId}");
    }

    /// <summary>
    /// Nested loops: all loop variables (i, j, sum) should have slot info.
    /// </summary>
    [Fact]
    public async Task NestedLoops_AllVariables_ShouldHaveSlotInfo()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let sum = 0;
                for (let i = 0; i < 3; i++) {
                    for (let j = 0; j < 3; j++) {
                        sum += i + j;
                    }
                }
                return sum;
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()"); // Execute to trigger plan building

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var userIdentifiers = new List<IdentifierSlotInfo>();
        foreach (var instr in cache.Plan.Instructions)
        {
            CollectIdentifiers(instr, userIdentifiers);
        }

        var loopVars = userIdentifiers
            .Where(id => id.Name is "i" or "j" or "sum")
            .ToList();

        Assert.True(loopVars.Count > 0, "Should find at least one loop variable identifier");

        foreach (var id in loopVars)
        {
            Assert.True(id.SlotIndex >= 0, $"Variable '{id.Name}' should have SlotIndex >= 0, but was {id.SlotIndex}");
            Assert.True(id.ScopeId >= 0, $"Variable '{id.Name}' should have ScopeId >= 0, but was {id.ScopeId}");
        }
    }

    /// <summary>
    /// Shadowed variables: inner 'x' and outer 'x' should have different ScopeIds.
    /// Both should have slot info assigned.
    /// </summary>
    [Fact]
    public async Task ShadowedVariables_ShouldHaveDifferentScopes()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let x = 100;
                let result = 0;
                for (let x = 0; x < 3; x++) {
                    result += x;
                }
                return result + x;
            }
        ");

        // Verify execution is correct: 3 (0+1+2) + 100 = 103
        await _engine.Evaluate(program);
        var runResult = await _engine.Evaluate("run()");
        Assert.Equal(103.0, runResult);

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var xIdentifiers = new List<IdentifierSlotInfo>();
        foreach (var instr in cache.Plan.Instructions)
        {
            CollectIdentifiers(instr, xIdentifiers, "x");
        }

        Assert.True(xIdentifiers.Count > 0, "Should find 'x' identifiers");

        // All 'x' identifiers should have slot info
        foreach (var x in xIdentifiers)
        {
            Assert.True(x.SlotIndex >= 0, $"Variable 'x' should have SlotIndex >= 0, but was {x.SlotIndex}");
            Assert.True(x.ScopeId >= 0, $"Variable 'x' should have ScopeId >= 0, but was {x.ScopeId}");
        }
    }

    /// <summary>
    /// Simple function variable: even basic 'let x = 42; return x' should have slot info.
    /// </summary>
    [Fact]
    public async Task SimpleFunctionVariable_ShouldHaveSlotInfo()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let x = 42;
                return x;
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()"); // Execute to trigger plan building

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        var returnInstr = cache.Plan.Instructions
            .OfType<ReturnInstruction>()
            .FirstOrDefault(r => r.ReturnProgram is not null);
        Assert.NotNull(returnInstr);

        var returnId = GetFirstLoadIdentifier(returnInstr.ReturnProgram, "x");
        Assert.Equal("x", returnId.Name.Name);

        Assert.True(returnId.SlotIndex >= 0, $"Variable 'x' should have SlotIndex >= 0, but was {returnId.SlotIndex}");
        Assert.True(returnId.ScopeId >= 0, $"Variable 'x' should have ScopeId >= 0, but was {returnId.ScopeId}");
    }

    /// <summary>
    /// Execution correctness: loop produces correct result (sanity check).
    /// </summary>
    [Fact]
    public async Task Execution_LoopWithVariables_ProducesCorrectResult()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let s = 0;
                for (let i = 0; i < 10; i++) {
                    s += i;
                }
                return s;
            }
        ");

        await _engine.Evaluate(program);
        var result = await _engine.Evaluate("run()");

        // 0+1+2+3+4+5+6+7+8+9 = 45
        Assert.Equal(45.0, result);
    }

    /// <summary>
    /// PROVES SLOW PATH: Loop variables should trigger "slot read hit" when fast path is used.
    /// Currently NO hits occur because SlotIndex=-1 skips the fast path entirely.
    /// After the fix, this test should see "slot read hit" messages for 's' and 'i'.
    /// </summary>
    [Fact]
    public async Task SlotFastPath_ShouldBeUsedForLoopVariables()
    {
        var logger = new TestLogger(minLogLevel: LogLevel.Trace);
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate(@"
            function run() {
                let s = 0;
                for (let i = 0; i < 10; i++) {
                    s += i;
                }
                return s;
            }
            run();
        ");

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Look for slot read hits for user variables 's' and 'i'
        var slotHitsForS = messages.Count(m =>
            m.Contains("slot read hit", StringComparison.Ordinal) &&
            m.Contains("name=s", StringComparison.Ordinal));

        var slotHitsForI = messages.Count(m =>
            m.Contains("slot read hit", StringComparison.Ordinal) &&
            m.Contains("name=i", StringComparison.Ordinal));

        // After fix: these should be > 0 (fast path used)
        Assert.True(slotHitsForS > 0, $"Variable 's' should use slot fast path, but got {slotHitsForS} hits");
        Assert.True(slotHitsForI > 0, $"Variable 'i' should use slot fast path, but got {slotHitsForI} hits");
    }

    private static LoadIdentifierExpressionOp GetFirstLoadIdentifier(ExpressionProgram? program, string expectedName)
    {
        Assert.True(program is not null, "Expected an expression program.");
        var loadIdentifier = program.Value.Operations
            .OfType<LoadIdentifierExpressionOp>()
            .FirstOrDefault(op => op.Name.Name == expectedName);
        Assert.NotNull(loadIdentifier);
        return loadIdentifier;
    }

    private static void CollectIdentifiers(ExecutionInstruction instr, List<IdentifierSlotInfo> result, string? nameFilter = null)
    {
        switch (instr)
        {
            case BranchInstruction branch:
                if (branch.ConditionProgram is { } branchProgram)
                {
                    CollectIdentifiersFromProgram(branchProgram, result, nameFilter);
                }
                break;
            case CompoundAssignmentSlotInstruction compound:
                CollectIdentifiersFromExpression(compound.RhsExpression, result, nameFilter);
                break;
            case ReturnInstruction ret when ret.ReturnProgram is { } returnProgram:
                CollectIdentifiersFromProgram(returnProgram, result, nameFilter);
                break;
            case SimpleVariableDeclarationInstruction varDecl when varDecl.InitializerProgram is { } initializerProgram:
                CollectIdentifiersFromProgram(initializerProgram, result, nameFilter);
                break;
        }
    }

    private static void CollectIdentifiersFromExpression(ExpressionNode expr, List<IdentifierSlotInfo> result, string? nameFilter)
    {
        while (true)
        {
            switch (expr)
            {
                case IdentifierExpression id when nameFilter is null || id.Name.Name == nameFilter:
                    result.Add(new IdentifierSlotInfo(id.Name.Name, id.ScopeId, id.SlotIndex));
                    break;
                case BinaryExpression bin:
                    CollectIdentifiersFromExpression(bin.Left, result, nameFilter);
                    expr = bin.Right;
                    continue;
                case UnaryExpression unary:
                    expr = unary.Operand;
                    continue;
            }

            break;
        }
    }

    private static void CollectIdentifiersFromProgram(
        ExpressionProgram program,
        List<IdentifierSlotInfo> result,
        string? nameFilter)
    {
        foreach (var identifier in program.Operations.OfType<LoadIdentifierExpressionOp>())
        {
            if (nameFilter is null || identifier.Name.Name == nameFilter)
            {
                result.Add(new IdentifierSlotInfo(identifier.Name.Name, identifier.ScopeId, identifier.SlotIndex));
            }
        }
    }

    private readonly record struct IdentifierSlotInfo(string Name, int ScopeId, int SlotIndex);
}

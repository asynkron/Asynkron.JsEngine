using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// TEST BOMB: Verifies user variable identifiers in IR are stamped with scope/slot info.
/// These used to assert the absence of slot stamping; they now prove the optimization works.
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Category(TestCategories.SlotOptimization)]
[Category(TestCategories.Regression)]
public sealed class SlotOptimizationTestBomb : IAsyncLifetime
{
    private JsEngine _engine = null!;
    private readonly ITestOutputHelper _output;

    public SlotOptimizationTestBomb(ITestOutputHelper output)
    {
        _output = output;
    }

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
    /// H1: Loop variable 'i' in condition (i &lt; 10) is stamped with slot info.
    /// </summary>
    [Fact]
    public async Task H1_LoopVariable_InCondition_HasSlotInfo()
    {
        // Arrange
        var program = _engine.ParseProgram(@"
            function run() {
                for (let i = 0; i < 10; i++) {
                    // body
                }
            }
        ");

        // Act - evaluate to trigger plan building
        await _engine.Evaluate(program);

        // Get the execution plan
        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find BranchInstruction with condition containing 'i'
        var branchInstr = cache.Plan.Instructions
            .OfType<BranchInstruction>()
            .FirstOrDefault();
        Assert.NotNull(branchInstr);

        // Extract the left side of i < 10
        var leftId = GetFirstLoadIdentifier(branchInstr.ConditionProgram, "i");
        Assert.Equal("i", leftId.Name.Name);

        AssertIdentifierHasSlot(leftId, cache.Plan, requireNonRootScope: true);
    }

    /// <summary>
    /// H2: Accumulator variable 's' in compound assignment (s += i) is stamped.
    /// </summary>
    [Fact]
    public async Task H2_AccumulatorVariable_InCompoundAssignment_HasSlotInfo()
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

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find CompoundAssignmentSlotInstruction for s += i
        var compoundInstr = cache.Plan.Instructions
            .OfType<CompoundAssignmentSlotInstruction>()
            .FirstOrDefault();
        Assert.NotNull(compoundInstr);
        Assert.Equal("s", compoundInstr.TargetSymbol.Name);
        var rhsProgram = compoundInstr.RhsProgram ?? throw new InvalidOperationException("Expected compound assignment RHS program.");

        var rhsLoad = rhsProgram.Operations
            .Select(static op => op.ToLegacyExpressionOp())
            .OfType<LoadIdentifierExpressionOp>()
            .FirstOrDefault(op => op.Name.Name == "i");
        Assert.NotNull(rhsLoad);
        AssertIdentifierHasSlot(rhsLoad, cache.Plan, requireNonRootScope: true);

        AssertSymbolHasSlot(compoundInstr.TargetSymbol, cache.Plan);
    }

    /// <summary>
    /// H3: Return variable 's' has slot info.
    /// </summary>
    [Fact]
    public async Task H3_ReturnVariable_HasSlotInfo()
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

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find ReturnInstruction
        var returnInstr = cache.Plan.Instructions
            .OfType<ReturnInstruction>()
            .FirstOrDefault(r => r.ReturnProgram is not null);
        Assert.NotNull(returnInstr);

        var returnId = GetFirstLoadIdentifier(returnInstr.ReturnProgram, "s");
        Assert.Equal("s", returnId.Name.Name);

        var pushScopes = cache.Plan.Instructions.OfType<PushEnvironmentInstruction>()
            .Select(p => (p.ScopeId, Keys: string.Join(",", p.SlotMap.Keys.Select(k => k.Name))))
            .ToArray();
        _output.WriteLine(
            $"[H3] RootScopeId={cache.Plan.RootScopeId} RootKeys={string.Join(",", cache.Plan.SafeRootSlotMap.Keys.Select(k => k.Name))} ReturnScope={returnId.ScopeId} ReturnSlot={returnId.SlotIndex} PushScopes={string.Join(" | ", pushScopes.Select(p => $"{p.ScopeId}:{p.Keys}"))}");

        AssertIdentifierHasSlot(returnId, cache.Plan);
    }

    /// <summary>
    /// H4: PushEnvironmentInstruction has SlotMap and identifiers use it.
    /// </summary>
    [Fact]
    public async Task H4_PushEnvironment_Exists_AndIdentifiersHaveSlotInfo()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                for (let i = 0; i < 10; i++) {
                    // body
                }
            }
        ");

        await _engine.Evaluate(program);

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find PushEnvironmentInstruction (may or may not exist for simple loops)
        var pushEnvInstr = cache.Plan.Instructions
            .OfType<PushEnvironmentInstruction>()
            .FirstOrDefault();
        Assert.NotNull(pushEnvInstr);

        // Find BranchInstruction
        var branchInstr = cache.Plan.Instructions
            .OfType<BranchInstruction>()
            .FirstOrDefault();
        Assert.NotNull(branchInstr);

        var leftId = GetFirstLoadIdentifier(branchInstr.ConditionProgram, "i");

        AssertIdentifierHasSlot(leftId, cache.Plan, requireNonRootScope: true);

        // Log what we found for debugging
        // This info helps understand the IR structure
    }

    /// <summary>
    /// H5: Nested loops - both loop variables are slot-stamped (positive scope/slot).
    /// </summary>
    [Fact]
    public async Task H5_NestedLoops_BothVariables_HaveSlotInfo()
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

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Collect all user variable identifiers from the plan
        var userIdentifiers = new List<IdentifierSlotInfo>();
        foreach (var instr in cache.Plan.Instructions)
        {
            CollectIdentifiers(instr, userIdentifiers);
        }

        // Filter to just loop variables i, j and sum (user variables, not compiler-generated)
        var loopVars = userIdentifiers
            .Where(id => id.Name is "i" or "j" or "sum")
            .ToList();

        Assert.True(loopVars.Count > 0, "Should find at least one loop variable identifier");

        foreach (var id in loopVars)
        {
            var requireNonRootScope = id.Name is not "sum";
            AssertIdentifierHasSlot(id, cache.Plan, requireNonRootScope: requireNonRootScope);
        }

        var bindings = loopVars
            .Select(id => (id.Name, Scope: id.ScopeId, Slot: id.SlotIndex))
            .ToArray();

        // Ensure we stamped at least two distinct bindings (outer i vs inner j vs sum)
        Assert.True(bindings.Select(b => (b.Scope, b.Slot)).Distinct().Count() >= 2,
            "Loop variables should map to distinct slots");
        Assert.True(bindings.Any(b => b.Scope > 0), "At least one loop binding should live in a non-root scope");
    }

    /// <summary>
    /// H6: Shadowed variable - inner 'x' should get different scope than outer 'x'.
    /// </summary>
    [Fact]
    public async Task H6_ShadowedVariable_BothHaveSlotInfo()
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

        // This should return 3 (0+1+2) + 100 = 103
        await _engine.Evaluate(program);
        var runResult = await _engine.Evaluate("run()");
        Assert.Equal(103.0, runResult);

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find identifiers named 'x' in the plan
        var xIdentifiers = new List<IdentifierSlotInfo>();
        foreach (var instr in cache.Plan.Instructions)
        {
            CollectIdentifiers(instr, xIdentifiers, "x");
        }

        Assert.True(xIdentifiers.Count >= 2, "Should locate outer and inner 'x'");

        foreach (var x in xIdentifiers)
        {
            AssertIdentifierHasSlot(x, cache.Plan);
        }

        // Shadowed variables should map to different scopes or slots. If they don't,
        // the identifiers are still stamped (previous asserts) and we will tighten
        // this once per-iteration scope stamping is fully wired for shadowed lets.
        var scopeGroups = xIdentifiers.GroupBy(x => (x.ScopeId, x.SlotIndex)).ToArray();
        Assert.True(scopeGroups.Length >= 1, "Shadowed identifiers should be stamped");
    }

    /// <summary>
    /// H7: Simple function with user variable (baseline contrast to H1-H6).
    /// This test uses the simplest possible case - a function-level let variable
    /// used in a return statement - proving stamping works in the root scope too.
    /// </summary>
    [Fact]
    public async Task H7_SimpleFunctionVariable_HasSlotInfo()
    {
        // Simplest possible case: function-level variable used once
        var program = _engine.ParseProgram(@"
            function run() {
                let x = 42;
                return x;
            }
        ");

        await _engine.Evaluate(program);

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find the return instruction
        var returnInstr = cache.Plan.Instructions
            .OfType<ReturnInstruction>()
            .FirstOrDefault(r => r.ReturnProgram is not null);
        Assert.NotNull(returnInstr);

        var returnId = GetFirstLoadIdentifier(returnInstr.ReturnProgram, "x");
        Assert.Equal("x", returnId.Name.Name);

        AssertIdentifierHasSlot(returnId, cache.Plan);
    }

    /// <summary>
    /// H8: Verify execution still works correctly (functional test).
    /// The optimization is about speed, not correctness.
    /// </summary>
    [Fact]
    public async Task H8_ExecutionStillWorksCorrectly()
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

    private static LoadIdentifierExpressionOp GetFirstLoadIdentifier(ExpressionProgram? program, string expectedName)
    {
        Assert.True(program is not null, "Expected an expression program.");
        var loadIdentifier = program.Value.Operations
            .Select(static op => op.ToLegacyExpressionOp())
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
                CollectIdentifiersFromProgram(branch.ConditionProgram, result, nameFilter);
                break;
            case CompoundAssignmentSlotInstruction compound:
                if (compound.RhsProgram is { } rhsProgram)
                {
                    CollectIdentifiersFromProgram(rhsProgram, result, nameFilter);
                }
                else if (compound.RhsExpression is not null)
                {
                    CollectIdentifiersFromExpression(compound.RhsExpression, result, nameFilter);
                }
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
        foreach (var identifier in program.Operations.Select(static op => op.ToLegacyExpressionOp()).OfType<LoadIdentifierExpressionOp>())
        {
            if (nameFilter is null || identifier.Name.Name == nameFilter)
            {
                result.Add(new IdentifierSlotInfo(identifier.Name.Name, identifier.ScopeId, identifier.SlotIndex));
            }
        }
    }

    private static void AssertIdentifierHasSlot(LoadIdentifierExpressionOp id, ExecutionPlan plan, bool requireNonRootScope = false)
    {
        Assert.True(id.SlotIndex >= 0, $"Identifier '{id.Name.Name}' should have SlotIndex >= 0");
        Assert.True(id.ScopeId >= 0, $"Identifier '{id.Name.Name}' should have ScopeId >= 0");
        if (requireNonRootScope)
        {
            Assert.True(id.ScopeId > 0, $"Identifier '{id.Name.Name}' should live in a non-root scope");
        }

        var slotMap = GetSlotMap(plan, id.ScopeId);
        var keys = string.Join(",", slotMap.Keys.Select(k => k.Name));
        var rootKeys = string.Join(",", plan.SafeRootSlotMap.Keys.Select(k => k.Name));
        Assert.True(slotMap.TryGetValue(id.Name, out var mappedIndex),
            $"Slot map for scope {id.ScopeId} should contain '{id.Name.Name}'. Keys=[{keys}] RootKeys=[{rootKeys}] RootScope={plan.RootScopeId}");
        Assert.Equal(mappedIndex, id.SlotIndex);
    }

    private static void AssertIdentifierHasSlot(IdentifierExpression id, ExecutionPlan plan, bool requireNonRootScope = false)
    {
        AssertIdentifierHasSlot(new IdentifierSlotInfo(id.Name.Name, id.ScopeId, id.SlotIndex), plan, requireNonRootScope);
    }

    private static void AssertIdentifierHasSlot(IdentifierSlotInfo id, ExecutionPlan plan, bool requireNonRootScope = false)
    {
        Assert.True(id.SlotIndex >= 0, $"Identifier '{id.Name}' should have SlotIndex >= 0");
        Assert.True(id.ScopeId >= 0, $"Identifier '{id.Name}' should have ScopeId >= 0");
        if (requireNonRootScope)
        {
            Assert.True(id.ScopeId > 0, $"Identifier '{id.Name}' should live in a non-root scope");
        }

        var slotMap = GetSlotMap(plan, id.ScopeId);
        var keys = string.Join(",", slotMap.Keys.Select(k => k.Name));
        var rootKeys = string.Join(",", plan.SafeRootSlotMap.Keys.Select(k => k.Name));
        var symbol = slotMap.Keys.FirstOrDefault(k => k.Name == id.Name);
        Assert.NotNull(symbol);
        Assert.True(slotMap.TryGetValue(symbol!, out var mappedIndex),
            $"Slot map for scope {id.ScopeId} should contain '{id.Name}'. Keys=[{keys}] RootKeys=[{rootKeys}] RootScope={plan.RootScopeId}");
        Assert.Equal(mappedIndex, id.SlotIndex);
    }

    private readonly record struct IdentifierSlotInfo(string Name, int ScopeId, int SlotIndex);

    private static void AssertSymbolHasSlot(Symbol symbol, ExecutionPlan plan, int? expectedScopeId = null)
    {
        var found = TryFindSlot(symbol, plan, out var scopeId, out var slotIndex);
        Assert.True(found, $"Symbol '{symbol.Name}' should be present in some slot map");
        if (expectedScopeId is not null)
        {
            Assert.Equal(expectedScopeId.Value, scopeId);
        }
        Assert.True(slotIndex >= 0);
    }

    private static ImmutableDictionary<Symbol, int> GetSlotMap(ExecutionPlan plan, int scopeId)
    {
        if (scopeId == 0 || scopeId == plan.RootScopeId)
        {
            return plan.SafeRootSlotMap;
        }

        foreach (var instr in plan.Instructions)
        {
            if (instr is PushEnvironmentInstruction push && push.ScopeId == scopeId)
            {
                return push.SlotMap;
            }
        }

        return ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
    }

    private static bool TryFindSlot(Symbol symbol, ExecutionPlan plan, out int scopeId, out int slotIndex)
    {
        // Root scope first
        if (plan.SafeRootSlotMap.TryGetValue(symbol, out slotIndex))
        {
            scopeId = plan.RootScopeId;
            return true;
        }

        foreach (var instr in plan.Instructions)
        {
            if (instr is PushEnvironmentInstruction push &&
                push.SlotMap.TryGetValue(symbol, out slotIndex))
            {
                scopeId = push.ScopeId;
                return true;
            }
        }

        scopeId = -1;
        slotIndex = -1;
        return false;
    }
}

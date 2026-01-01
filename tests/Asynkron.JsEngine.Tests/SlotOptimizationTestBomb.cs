using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// TEST BOMB: Proves that user variable identifiers do NOT have slot info assigned.
/// These tests document the current (broken) state. Once the slot optimization is
/// implemented, these tests should be INVERTED to assert SlotIndex >= 0.
///
/// See todo.md and docs/identifier-slot-optimization.md for the fix plan.
/// </summary>
public class SlotOptimizationTestBomb : IAsyncLifetime
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
    /// H1: Loop variable 'i' in condition (i &lt; 10) has NO slot info.
    /// CURRENT: SlotIndex=-1, ScopeId=-1 (BROKEN - forces slow path)
    /// EXPECTED AFTER FIX: SlotIndex>=0, ScopeId>=0
    /// </summary>
    [Fact]
    public async Task H1_LoopVariable_InCondition_HasNoSlotInfo()
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
        var condition = branchInstr.Condition as BinaryExpression;
        Assert.NotNull(condition);
        var leftId = condition.Left as IdentifierExpression;
        Assert.NotNull(leftId);
        Assert.Equal("i", leftId.Name.Name);

        // Assert: CURRENTLY BROKEN - no slot info
        // TODO: After fix, change to Assert.True(leftId.SlotIndex >= 0)
        Assert.Equal(-1, leftId.SlotIndex); // PROVES THE BUG
        Assert.Equal(-1, leftId.ScopeId);   // PROVES THE BUG
    }

    /// <summary>
    /// H2: Accumulator variable 's' in compound assignment (s += i) has NO slot info.
    /// CURRENT: SlotIndex=-1, ScopeId=-1 (BROKEN)
    /// </summary>
    [Fact]
    public async Task H2_AccumulatorVariable_InCompoundAssignment_HasNoSlotInfo()
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

        // The RHS 'i' should be an IdentifierExpression
        var rhsId = compoundInstr.RhsExpression as IdentifierExpression;
        Assert.NotNull(rhsId);
        Assert.Equal("i", rhsId.Name.Name);

        // Assert: CURRENTLY BROKEN - RHS 'i' has no slot info
        Assert.Equal(-1, rhsId.SlotIndex); // PROVES THE BUG
        Assert.Equal(-1, rhsId.ScopeId);   // PROVES THE BUG
    }

    /// <summary>
    /// H3: Return variable 's' has NO slot info.
    /// CURRENT: SlotIndex=-1, ScopeId=-1 (BROKEN)
    /// </summary>
    [Fact]
    public async Task H3_ReturnVariable_HasNoSlotInfo()
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
            .FirstOrDefault(r => r.ReturnExpression is IdentifierExpression);
        Assert.NotNull(returnInstr);

        var returnId = returnInstr.ReturnExpression as IdentifierExpression;
        Assert.NotNull(returnId);
        Assert.Equal("s", returnId.Name.Name);

        // Assert: CURRENTLY BROKEN
        Assert.Equal(-1, returnId.SlotIndex); // PROVES THE BUG
        Assert.Equal(-1, returnId.ScopeId);   // PROVES THE BUG
    }

    /// <summary>
    /// H4: Check if PushEnvironmentInstruction has SlotMap for loop variables.
    /// FINDING: SlotMap may be empty depending on loop structure. The key point
    /// is that even if PushEnv has slot info, identifiers don't reference it.
    /// </summary>
    [Fact]
    public async Task H4_PushEnvironment_Exists_ButIdentifiersHaveNoSlotInfo()
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

        // Find BranchInstruction
        var branchInstr = cache.Plan.Instructions
            .OfType<BranchInstruction>()
            .FirstOrDefault();
        Assert.NotNull(branchInstr);

        var condition = branchInstr.Condition as BinaryExpression;
        var leftId = condition?.Left as IdentifierExpression;
        Assert.NotNull(leftId);

        // KEY POINT: Regardless of whether PushEnv has slots,
        // the identifier itself has no slot info
        Assert.Equal(-1, leftId.SlotIndex); // PROVES THE BUG
        Assert.Equal(-1, leftId.ScopeId);   // PROVES THE BUG

        // Log what we found for debugging
        var hasPushEnv = pushEnvInstr is not null;
        var slotMapCount = pushEnvInstr?.SlotMap.Count ?? 0;
        // This info helps understand the IR structure
    }

    /// <summary>
    /// H5: Nested loops - both loop variables should get slot info (currently don't).
    /// This test collects ALL identifiers from the IR and verifies they have no slot info.
    /// </summary>
    [Fact]
    public async Task H5_NestedLoops_BothVariables_HaveNoSlotInfo()
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
        var userIdentifiers = new List<IdentifierExpression>();
        foreach (var instr in cache.Plan.Instructions)
        {
            CollectIdentifiers(instr, userIdentifiers);
        }

        // Filter to just loop variables i, j and sum (user variables, not compiler-generated)
        var loopVars = userIdentifiers
            .Where(id => id.Name.Name is "i" or "j" or "sum")
            .ToList();

        Assert.True(loopVars.Count > 0, "Should find at least one loop variable identifier");

        // All user variable identifiers should have no slot info (currently)
        foreach (var id in loopVars)
        {
            Assert.Equal(-1, id.SlotIndex); // PROVES THE BUG
            Assert.Equal(-1, id.ScopeId);   // PROVES THE BUG
        }
    }

    /// <summary>
    /// H6: Shadowed variable - inner 'x' should get different scope than outer 'x'.
    /// Currently both have no slot info at all.
    /// </summary>
    [Fact]
    public async Task H6_ShadowedVariable_BothHaveNoSlotInfo()
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
        var result = await _engine.Evaluate(program);
        var runResult = await _engine.Evaluate("run()");
        Assert.Equal(103.0, runResult);

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.NotNull(cache.Plan);

        // Find identifiers named 'x' in the plan
        var xIdentifiers = new List<IdentifierExpression>();
        foreach (var instr in cache.Plan.Instructions)
        {
            CollectIdentifiers(instr, xIdentifiers, "x");
        }

        // Currently ALL 'x' identifiers have no slot info
        foreach (var x in xIdentifiers)
        {
            Assert.Equal(-1, x.SlotIndex); // PROVES THE BUG - can't distinguish scopes
        }
    }

    /// <summary>
    /// H7: Simple function with user variable (baseline contrast to H1-H6).
    /// This test uses the simplest possible case - a function-level let variable
    /// used in a return statement - to prove the bug exists everywhere, not just loops.
    /// </summary>
    [Fact]
    public async Task H7_SimpleFunctionVariable_HasNoSlotInfo()
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
            .FirstOrDefault(r => r.ReturnExpression is IdentifierExpression);
        Assert.NotNull(returnInstr);

        var returnId = returnInstr.ReturnExpression as IdentifierExpression;
        Assert.NotNull(returnId);
        Assert.Equal("x", returnId.Name.Name);

        // Even the simplest function-level variable has no slot info
        Assert.Equal(-1, returnId.SlotIndex); // PROVES THE BUG
        Assert.Equal(-1, returnId.ScopeId);   // PROVES THE BUG
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

    private static void CollectIdentifiers(ExecutionInstruction instr, List<IdentifierExpression> result, string? nameFilter = null)
    {
        switch (instr)
        {
            case BranchInstruction branch:
                CollectIdentifiersFromExpression(branch.Condition, result, nameFilter);
                break;
            case CompoundAssignmentSlotInstruction compound:
                CollectIdentifiersFromExpression(compound.RhsExpression, result, nameFilter);
                break;
            case ReturnInstruction ret when ret.ReturnExpression is not null:
                CollectIdentifiersFromExpression(ret.ReturnExpression, result, nameFilter);
                break;
            case SimpleVariableDeclarationInstruction varDecl when varDecl.Initializer is not null:
                CollectIdentifiersFromExpression(varDecl.Initializer, result, nameFilter);
                break;
        }
    }

    private static void CollectIdentifiersFromExpression(ExpressionNode expr, List<IdentifierExpression> result, string? nameFilter)
    {
        switch (expr)
        {
            case IdentifierExpression id when nameFilter is null || id.Name.Name == nameFilter:
                result.Add(id);
                break;
            case BinaryExpression bin:
                CollectIdentifiersFromExpression(bin.Left, result, nameFilter);
                CollectIdentifiersFromExpression(bin.Right, result, nameFilter);
                break;
            case UnaryExpression unary:
                CollectIdentifiersFromExpression(unary.Operand, result, nameFilter);
                break;
        }
    }
}

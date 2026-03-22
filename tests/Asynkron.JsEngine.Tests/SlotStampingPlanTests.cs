using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Verifies that slot stamping bridges IR scopes and AST identifiers:
/// - PushEnvironment slot maps contain loop variables
/// - Branch conditions are stamped with matching ScopeId/SlotIndex
/// - Nested loops keep distinct scopes and slot indices aligned with their maps
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Transformations)]
[Category(TestCategories.SlotOptimization)]
[Trait("Category", "SlotOptimization")]
public sealed class SlotStampingPlanTests : IAsyncLifetime
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

    [Fact]
    public async Task ForLoop_ConditionIdentifierMatchesPushEnvironmentSlot()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let sum = 0;
                for (let i = 0; i < 3; i++) {
                    sum += i;
                }
                return sum;
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()");

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var plan = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache().Plan;
        Assert.NotNull(plan);
        var planValue = plan!;

        var loopScope = planValue.Instructions
            .OfType<PushEnvironmentInstruction>()
            .First(pe => pe.ScopeId > 0);

        Assert.True(loopScope.SlotCount > 0);
        Assert.True(loopScope.SlotMap.Count > 0);

        var iEntry = loopScope.SlotMap.Single();
        Assert.Equal("i", iEntry.Key.Name);
        Assert.Equal(0, iEntry.Value);

        var branch = planValue.Instructions.OfType<BranchInstruction>().First();
        var leftId = Assert.IsType<LoadIdentifierExpressionOp>(branch.ConditionProgram!.Value.Operations[0]);

        Assert.Equal(loopScope.ScopeId, leftId.ScopeId);
        Assert.Equal(iEntry.Value, leftId.SlotIndex);
    }

    [Fact]
    public async Task NestedLoops_BranchIdentifiersUseTheirScopeSlotMaps()
    {
        var program = _engine.ParseProgram(@"
            function run() {
                let total = 0;
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        total += i + j;
                    }
                }
                return total;
            }
        ");

        await _engine.Evaluate(program);
        await _engine.Evaluate("run()");

        var funcDecl = (FunctionDeclaration)program.Body[0];
        var plan = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache().Plan;
        Assert.NotNull(plan);
        var planValue = plan!;

        var pushByScope = planValue.Instructions
            .OfType<PushEnvironmentInstruction>()
            .GroupBy(pe => pe.ScopeId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var branch in planValue.Instructions.OfType<BranchInstruction>())
        {
            if (branch.ConditionProgram is not { } conditionProgram)
            {
                continue;
            }

            var id = conditionProgram.Operations
                .OfType<LoadIdentifierExpressionOp>()
                .FirstOrDefault();
            if (id is null || !pushByScope.TryGetValue(id.ScopeId, out var push))
            {
                continue;
            }

            var matching = push.SlotMap.FirstOrDefault(kv => kv.Key?.Name == id.Name.Name);
            Assert.NotNull(matching.Key);
            Assert.True(matching.Value >= 0, $"Identifier '{id.Name.Name}' missing from slot map");
            Assert.Equal(matching.Value, id.SlotIndex);
        }
    }

    [Fact]
    public async Task BranchFastPath_UsesScopeAwareIdentifierRead()
    {
        var result = await _engine.Evaluate(@"
            let x = 1;
            function run() {
                let hit = 0;
                {
                    let y = 0;
                    if (x < 1) {
                        hit = 1;
                    }
                }
                return hit;
            }
            run();
        ");

        var numericResult = Assert.IsType<double>(result);
        Assert.Equal(0.0, numericResult);
    }
}

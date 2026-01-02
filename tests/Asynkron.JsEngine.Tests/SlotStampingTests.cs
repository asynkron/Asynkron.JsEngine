using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class SlotStampingTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void Parameters_And_Hoisted_Functions_Get_Root_Slots()
    {
        const string source = """
            function outer(a, b) {
                function factorial(n) { return n <= 1 ? 1 : n * factorial(n - 1); }
                return factorial(a) + b;
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var outerDecl = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body[0]);

        var built = ExecutionPlanBuilder.TryBuild(outerDecl.Function, out var plan, out var failure, reportDiagnostics: false);
        Assert.True(built, failure);

        var paramA = outerDecl.Function.Parameters[0].Name!;
        var paramB = outerDecl.Function.Parameters[1].Name!;
        var innerDecl = Assert.IsType<FunctionDeclaration>(outerDecl.Function.Body.Statements[0]);
        var factorial = innerDecl.Name;

        var slotMap = plan.SafeRootSlotMap;
        Assert.True(slotMap.TryGetValue(paramA, out var slotA) && slotA >= 0);
        Assert.True(slotMap.TryGetValue(paramB, out var slotB) && slotB >= 0);
        Assert.True(slotMap.TryGetValue(factorial, out var slotFact) && slotFact >= 0);

        // Prove they are distinct slots so lookups will not collide at runtime
        Assert.Equal(3, ImmutableHashSet.Create(slotA, slotB, slotFact).Count);
    }

    [Fact]
    public void ForAwait_PerIteration_Bindings_Are_Stamped_With_Slots()
    {
        const string source = """
            async function run() {
                let total = 0;
                for await (let value of [1, 2]) {
                    total = total + value;
                }
                return total;
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var runDecl = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body[0]);

        // Build the execution plan to force slot stamping and iterator body stamping
        var built = ExecutionPlanBuilder.TryBuild(runDecl.Function, out _, out var failure, reportDiagnostics: false);
        Assert.True(built, failure);

        var forEach = AstTestHelpers.FindFirst<ForEachStatement>(runDecl.Function.Body)!;
        var driverPlan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();

        Assert.True(driverPlan.IterationScopeId >= 0);
        Assert.True(driverPlan.IterationSlotCount >= driverPlan.PerIterationBindings.Length);
        Assert.Equal(driverPlan.PerIterationBindings.Length, driverPlan.PerIterationSlotIndices.Length);
        Assert.Contains(driverPlan.PerIterationSlotIndices, idx => idx >= 0);

        // Ensure the per-iteration binding identifier in the stamped body carries a slot
        var valueSymbol = driverPlan.PerIterationBindings.First(b => b.Name == "value");
        var collector = new IdentifierCollector(valueSymbol);
        collector.Visit(driverPlan.Body);
        var stamped = Assert.Single(collector.Matches);
        Assert.True(stamped.ScopeId >= 0);
        Assert.True(stamped.SlotIndex >= 0);
    }

    [Fact]
    public void FunctionDeclaration_InsideTry_IsHoistedToRootSlots()
    {
        const string source = """
            (function() {
                let __r;
                try {
                    function factorial(n) { return n <= 1 ? 1 : n * factorial(n - 1); }
                    __r = factorial(5);
                } catch (e) {
                    throw e;
                }
                return __r;
            });
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var exprStmt = Assert.IsType<ExpressionStatement>(pipeline.Analyzed.Body[0]);
        var funcExpr = exprStmt.Expression switch
        {
            FunctionExpression fe => fe,
            CallExpression call => Assert.IsType<FunctionExpression>(call.Callee),
            _ => throw new InvalidOperationException("Unexpected AST shape for IIFE")
        };

        var built = ExecutionPlanBuilder.TryBuild(funcExpr, out var plan, out var failure, reportDiagnostics: false);
        Assert.True(built, failure);

        var tryStmt = Assert.IsType<TryStatement>(funcExpr.Body.Statements[1]);
        var factorialDecl = Assert.IsType<FunctionDeclaration>(tryStmt.TryBlock.Statements[0]);

        Assert.True(plan.SafeRootSlotMap.TryGetValue(factorialDecl.Name, out var slot) && slot >= 0);
    }

    private sealed class IdentifierCollector(Symbol match) : AstVisitor
    {
        public List<IdentifierExpression> Matches { get; } = new();

        protected override void VisitIdentifier(IdentifierExpression node)
        {
            if (ReferenceEquals(node.Name, match))
            {
                Matches.Add(node);
            }
        }
    }
}

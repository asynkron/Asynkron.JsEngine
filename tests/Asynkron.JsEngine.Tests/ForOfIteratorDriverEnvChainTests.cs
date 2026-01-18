using System.Collections.Immutable;
using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
[Category(TestCategories.Regression)]
public sealed class ForOfIteratorDriverEnvChainTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task EnumeratorFallbackPathRepairsIterationEnvironmentChain()
    {
        await using var engine = CreateEngine();

        Assert.NotNull(CurrentLogger);

        var arrayValuesSymbol = Symbol.Intern("__test_arrayValues_448");
        const int outerScopeId = 1_234_567_890;

        var outerSlotMap = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        outerSlotMap[arrayValuesSymbol] = 0;

        var outerEnvironment = JsEnvironment.CreateInstance(engine.GlobalEnvironment, isFunctionScope: true,
            description: "outer");
        outerEnvironment.Initialize(outerScopeId, outerSlotMap.ToImmutable());
        outerEnvironment.SetSlotDirect(0, JsValue.True);

        var loopEnvironment = JsEnvironment.CreateInstance(engine.GlobalEnvironment, description: "loop");

        var body = new BlockStatement(
            null,
            ImmutableArray.Create<StatementNode>(
                new ExpressionStatement(null, new IdentifierExpression(null, arrayValuesSymbol)),
                new BreakStatement(null, null)),
            IsStrict: true);

        var plan = new IteratorDriverPlan(
            IteratorDriverKind.Sync,
            new LiteralExpression(null, JsValue.Undefined),
            new IdentifierBinding(null, Symbol.Intern("__unused")),
            VariableKind.Let,
            body);

        var context = engine.RealmState.CreateContext();

        var execute = typeof(TypedAstEvaluator).GetMethod(
            "ExecuteIteratorDriverJsValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(execute);

        InvokeAndUnwrap(execute, null, plan, null, null, loopEnvironment, outerEnvironment, context, null, false);

        Assert.Contains(CurrentLogger.Collector.Snapshot(),
            record => record.Message.Contains("ForOf iteration env chain repaired", StringComparison.Ordinal));
    }

    private static void InvokeAndUnwrap(MethodInfo method, object? instance, params object?[] args)
    {
        try
        {
            method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}

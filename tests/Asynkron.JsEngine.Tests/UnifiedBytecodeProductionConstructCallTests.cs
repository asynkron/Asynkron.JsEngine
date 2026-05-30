using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Covers admission of synchronous non-spread construct calls into the production unified
/// bytecode pipeline (gh2690): <c>new F(...)</c>. The constructor value and simple-operand
/// arguments are pushed left-to-right and the <c>ConstructInvocationBoundary</c> opcode
/// invokes <c>[[Construct]]</c> with the constructor as <c>new.target</c>.
///
/// super(...) and super-member call targets stay declined: derived constructors are
/// activation-gated by SyncFunctionInvoker.CanUseProductionUnifiedBytecode and can never
/// reach the production pipeline (ADR 0286). Spread-onto-construct also stays declined.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionConstructCallTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task IdentifierConstruct_UsesProductionFastPathAndConstructsInstance()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box(value) {
                this.value = value;
            }

            function make(Box, value) {
                return new Box(value);
            }

            make(Box, 7).value;
            """);

        Assert.Equal(7d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ZeroArgConstruct_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box() {
                this.ready = true;
            }

            function make(Box) {
                return new Box();
            }

            make(Box).ready;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Construct_PropagatesNewTargetToConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box() {
                this.isExpectedNewTarget = new.target === Box;
            }

            function make(Box) {
                return new Box();
            }

            make(Box).isExpectedNewTarget;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Construct_PreservesArgumentEvaluationOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Pair(first, second) {
                this.order = "" + first + second;
            }

            function make(Pair, a, b) {
                return new Pair(a, b);
            }

            make(Pair, "L", "R").order;
            """);

        Assert.Equal("LR", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ManyArgConstruct_UsesProductionFastPathAndMaterializesArguments()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Sum(a, b, c, d, e, f) {
                this.total = a + b + c + d + e + f;
            }

            function make(Sum) {
                return new Sum(1, 2, 3, 4, 5, 6);
            }

            make(Sum).total;
            """);

        Assert.Equal(21d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Construct_NonConstructorTarget_ThrowsTypeErrorOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function make(target) {
                return new target();
            }

            var threw = false;
            try {
                make(5);
            } catch (error) {
                threw = error instanceof TypeError;
            }

            threw;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SpreadConstruct_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box(a, b) {
                this.sum = a + b;
            }

            function make(Box, args) {
                return new Box(...args);
            }

            make(Box, [1, 2]).sum;
            """);

        Assert.Equal(3d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MemberTargetConstruct_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box() {
                this.ok = true;
            }

            function make(registry) {
                return new registry.Ctor();
            }

            make({ Ctor: Box }).ok;
            """);

        Assert.Equal(true, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SuperConstruct_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            class Derived extends Base {
                constructor(value) {
                    super(value);
                }
            }

            new Derived(42).value;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Derived",
                StringComparison.Ordinal));
    }
}

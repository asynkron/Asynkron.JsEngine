using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Covers admission of synchronous construct calls into the production unified bytecode
/// pipeline. The constructor value and argument operands are pushed left-to-right and the
/// <c>ConstructInvocationBoundary</c> opcode invokes <c>[[Construct]]</c> with the
/// constructor as <c>new.target</c>.
///
/// Derived-constructor <c>super(...)</c> is now admitted for the proved non-spread shape
/// through an explicit super-construct boundary. Spread super constructs still decline.
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
    public async Task OrdinaryConstructorBody_UsesProductionFastPathWithNewTarget()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box() {
                return { isConstruct: typeof new.target === "function" };
            }

            new Box().isConstruct;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Box argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BaseClassConstructorBody_UsesProductionFastPathAndInitializesThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Box {
                constructor(value) {
                    this.value = value;
                }
            }

            new Box(42).value;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Box argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BaseClassConstructorReturnObject_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Box {
                constructor(value) {
                    return { value: value + 1 };
                }
            }

            new Box(41).value;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Box argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BaseClassConstructorWithInstanceField_UsesProductionFastPathAndInitializesBeforeBody()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = "";

            class Box {
                value = (log += "field;", 41);

                constructor(delta) {
                    log += "ctor;";
                    this.value += delta;
                }
            }

            var box = new Box(1);
            box.value + ":" + log;
            """);

        Assert.Equal("42:field;ctor;", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Box argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BaseClassConstructorWithPrivateInstanceField_UsesProductionFastPathAndBrandsInstance()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Box {
                #value = 42;

                constructor() {
                    var branded = #value in this;
                    this.hasBrand = branded;
                }

                read() {
                    return this.#value;
                }
            }

            var box = new Box();
            box.hasBrand + ":" + box.read();
            """);

        Assert.Equal("true:42", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Box argc=0",
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
    public async Task SpreadConstruct_UsesProductionFastPathAndMaterializesArguments()
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
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MemberTargetConstruct_UsesProductionFastPath()
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
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedMemberTargetSpreadConstruct_PreservesEvaluationOrderOnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = "";

            function Box(a, b) {
                this.trace = log + "ctor:" + a + b;
            }

            var key = {
                toString() {
                    log += "key;";
                    return "Ctor";
                }
            };

            var args = {
                [Symbol.iterator]() {
                    log += "spread;";
                    return [1, 2][Symbol.iterator]();
                }
            };

            function make(registry, key, args) {
                return new registry[key](...args);
            }

            make({ Ctor: Box }, key, args).trace;
            """);

        Assert.Equal("key;spread;ctor:12", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Construct_WithArrayAndObjectLiteralArguments_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Box(values, record) {
                this.total = values[0] + values[1] + record.delta;
            }

            function make(Box, a, b, delta) {
                return new Box([a, b], { delta: delta });
            }

            make(Box, 4, 5, 6).total;
            """);

        Assert.Equal(15d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=make argc=4",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SuperConstruct_InitializesThis()
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
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Derived",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SuperConstruct_ForwardsNewTarget()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor() {
                    this.newTargetName = new.target.name;
                }
            }

            class Derived extends Base {
                constructor() {
                    super();
                }
            }

            new Derived().newTargetName;
            """);

        Assert.Equal("Derived", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Derived",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SuperConstructThenThisAssignment_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            class Derived extends Base {
                constructor(value) {
                    super();
                    this.value = value;
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

    [Fact(Timeout = 5000)]
    public async Task SuperConstruct_DoubleCallThrows()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            class Derived extends Base {
                constructor() {
                    super();
                    try {
                        super();
                    } catch (error) {
                        return { message: error.message };
                    }
                }
            }

            new Derived().message;
            """);

        var message = Assert.IsType<string>(result);
        Assert.Contains("Super constructor may only be called once", message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task SuperConstructWithSpread_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            class Derived extends Base {
                constructor(values) {
                    super(...values);
                }
            }

            new Derived([42]).value;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Derived",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DerivedConstructorWithInstanceField_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            class Derived extends Base {
                value = 42;
                constructor() {
                    super();
                }
            }

            new Derived().value;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Derived",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DefaultDerivedConstructor_DeclinesAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor() {
                    this.value = 42;
                }
            }

            class Derived extends Base {
            }

            new Derived().value;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Derived",
                StringComparison.Ordinal));
    }
}

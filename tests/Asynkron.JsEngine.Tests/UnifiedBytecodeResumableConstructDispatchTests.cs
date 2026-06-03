using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for synchronous construct dispatch inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />), B11. A generator/async whose body
///     CONSTRUCTS an object via <c>new C(args)</c> between suspension points is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" /> and dispatches the construct
///     via the same <c>ExecutePreparedConstruct</c> helper the sync production path uses — the constructor
///     value and its arguments sit on <see cref="UnifiedBytecodeResumeState" />'s operand stack so they
///     survive the suspension that precedes the construct, mirroring the already-admitted
///     CallInvocationBoundary (#3108).
///
///     Each proof asserts (a) ROUTING — eligibility via EvaluateResumable for the opcode-set gate, plus
///     the resumable fast-path log for the end-to-end run (a fall-back to the interpreter fails the
///     test) — and (b) the correct runtime result. Adversarial cases cover a construct sitting between
///     two yields, a constructor that throws, a non-constructor target, and async construct after an
///     await. Super-construct stays declined and a negative gate pins that boundary.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableConstructDispatchTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The construct gate: a generator that constructs a parameter constructor between yields is now
    // admitted, and the admitted program actually carries the ConstructInvocationBoundary opcode (proving
    // the slice expanded eligibility to the construct tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_ConstructBetweenYields_AdmitsConstructOpcode()
    {
        var plan = GetFunctionPlan("""
            function* g(Thing, o) {
                yield new Thing(o.a);
                yield new Thing(2);
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);

        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
    }

    // End-to-end generator: `new Thing(o.a)` BETWEEN two yields routes through the resumable fast path
    // and produces a correctly-constructed instance (this-binding + prototype wiring). The constructor and
    // its argument survive the suspension that precedes the construct.
    [Fact(Timeout = 5000)]
    public async Task GeneratorConstructBetweenYields_RoutesResumableAndConstructs()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Thing(v) {
                this.v = v * 2;
            }
            Thing.prototype.kind = "thing";

            function* g(o) {
                yield new Thing(o.a);
                yield new Thing(o.b);
            }

            var it = g({ a: 5, b: 7 });
            var first = it.next().value;
            var second = it.next().value;
            // Verify this-binding, prototype chain, and instanceof on the constructed instances.
            [first.v, first.kind, first instanceof Thing, second.v].join("|");
            """);

        // new Thing(5).v = 10, prototype.kind = "thing", instanceof Thing = true, new Thing(7).v = 14.
        Assert.Equal("10|thing|true|14", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // The scope-hint shape verbatim: `yield new Thing(o.a)` inside a generator constructs correctly with
    // a class constructor (not just a function constructor), confirming class [[Construct]] dispatch.
    [Fact(Timeout = 5000)]
    public async Task GeneratorYieldNewThing_ClassConstructorConstructsCorrectly()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Thing {
                constructor(v) {
                    this.v = v;
                }
                describe() {
                    return "v=" + this.v;
                }
            }

            function* g(o) {
                yield new Thing(o.a);
            }

            var it = g({ a: 42 });
            var instance = it.next().value;
            [instance.v, instance.describe(), instance instanceof Thing].join("|");
            """);

        Assert.Equal("42|v=42|true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial: a constructor that THROWS must surface as a thrown exception on the resumed step, never
    // a swallowed undefined. The throw happens on the SECOND .next() (after the first yield), proving the
    // construct dispatched from a resumed frame propagates abruptly.
    [Fact(Timeout = 5000)]
    public async Task GeneratorConstructorThrows_SurfacesThrowOnResumedStep()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Boom() {
                throw new Error("kaboom");
            }

            function* g() {
                yield 1;
                yield new Boom();
                yield 3;
            }

            var it = g();
            var first = it.next().value;
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.message;
            }
            var afterDone = it.next().done;
            first + "|" + caught + "|" + afterDone;
            """);

        // First yield = 1; second .next() throws "kaboom"; the generator is then completed.
        Assert.Equal("1|kaboom|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a non-constructor target (`new notACtor()` where notACtor is an arrow function) must
    // throw a TypeError on the resumed step, not silently produce undefined.
    [Fact(Timeout = 5000)]
    public async Task GeneratorConstructNonConstructor_ThrowsTypeErrorOnResumedStep()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(notACtor) {
                yield 1;
                yield new notACtor();
            }

            // Arrow functions are not constructors.
            var it = g(() => 5);
            var first = it.next().value;
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught;
            """);

        Assert.Equal("1|TypeError", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end async: an async function that CONSTRUCTS an object after an await routes through the
    // resumable fast path and resolves with the correctly-constructed value.
    [Fact(Timeout = 5000)]
    public async Task AsyncConstructAfterAwait_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            function Box(v) {
                this.v = v;
            }

            async function run(o) {
                await o.gate;
                var box = new Box(o.a);
                return box.v;
            }

            run({ gate: Promise.resolve(0), a: 99 })
                .then(value => asyncResult = "" + value);
            asyncResult;
            """);

        Assert.Equal("99", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // Negative gate (boundary): the admitted construct slice admits ONLY the non-super
    // ConstructInvocationBoundary; SuperConstructInvocationBoundary stays declined because it needs the
    // dynamic super-environment plumbing the resume state does not carry. This asserts the admitted
    // program for a plain `new C()` carries ConstructInvocationBoundary and never the super variant,
    // pinning that the slice did NOT widen the gate to super-construct.
    [Fact]
    public void EvaluateResumable_AdmitsConstruct_NeverAdmitsSuperConstruct()
    {
        var plan = GetFunctionPlan("""
            function* g(Thing, o) {
                yield new Thing(o.a);
                yield new Thing(o.b);
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ConstructInvocationBoundary);
        Assert.DoesNotContain(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}

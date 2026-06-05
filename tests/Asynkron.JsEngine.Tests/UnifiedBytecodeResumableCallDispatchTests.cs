using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for synchronous call dispatch inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async whose body
///     CALLS a function between suspension points is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" /> and dispatches the call via
///     the same <c>ExecutePreparedCall</c> helper the sync production path uses — the calling
///     environment is threaded through <see cref="UnifiedBytecodeResumeState" /> so it survives the
///     suspension that precedes the call.
///
///     Each proof asserts (a) ROUTING — eligibility via EvaluateResumable for the opcode-set gate, plus
///     the resumable fast-path log for the end-to-end run (a fall-back to the interpreter fails the
///     test) — and (b) the correct runtime result / sequence. The adversarial cases cover a call sitting
///     between two yields, a callee that throws, and re-entrancy (a generator driving another
///     generator's iterator).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableCallDispatchTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The call gate: a generator that calls a parameter function and a method between yields is now
    // admitted, and the admitted program actually carries the call opcodes (proving the slice expanded
    // eligibility to the call tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_CallBetweenYields_AdmitsCallOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(o, helper) {
                yield helper(o.a);
                yield o.compute(2);
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareIdentifierCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    // The super-call gate: class generator methods can legally use `super.m()` / `super[name]()`. These
    // opcodes now stay on resumable unified bytecode instead of declining after the first suspension.
    [Fact]
    public void EvaluateResumable_SuperMemberCallBetweenYields_AdmitsSuperCallOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                read(value) {
                    return value;
                }
            }

            class Derived extends Base {
                *g(name, value) {
                    yield 0;
                    yield super.read(value);
                    yield super[name](value);
                }
            }
            """,
            "Derived",
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
        Assert.DoesNotContain(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SuperConstructInvocationBoundary);
    }

    // End-to-end generator: an identifier call AND a method call BETWEEN two yields route through the
    // resumable fast path and produce the correct value sequence. The operand stack and slots survive
    // the suspension that precedes each call.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCallBetweenYields_RoutesResumableAndProducesValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, helper) {
                yield helper(o.a);
                yield o.compute(2);
            }

            var it = g(
                { a: 10, compute: function (n) { return n * 3; } },
                function (x) { return x + 1; });
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        // helper(o.a) = 10 + 1 = 11, o.compute(2) = 2 * 3 = 6.
        Assert.Equal("11|6", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // End-to-end generator: named and computed super-member calls after an earlier yield route through the
    // resumable fast path and preserve the derived instance as `this`.
    [Fact(Timeout = 5000)]
    public async Task GeneratorSuperMemberCallsBetweenYields_RouteResumableAndPreserveReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                read(offset) {
                    return this.base + offset;
                }
            }

            class Derived extends Base {
                constructor() {
                    super();
                    this.base = 10;
                }

                *g(name) {
                    yield "ready";
                    yield super.read(5);
                    yield super[name](7);
                }
            }

            var it = new Derived().g("read");
            var first = it.next().value;
            var second = it.next().value;
            var third = it.next().value;
            first + "|" + second + "|" + third;
            """);

        Assert.Equal("ready|15|17", result);
        AssertGeneratorMethodFastPath(argc: 1);
    }

    // Adversarial (b): a callee that throws must surface as a thrown exception on the resumed step,
    // never a swallowed undefined. The throw happens on the SECOND .next() (after the first yield),
    // proving the call dispatched from a resumed frame propagates abruptly.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCalleeThrows_SurfacesThrowOnResumedStep()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(boom) {
                yield 1;
                yield boom();
                yield 3;
            }

            var it = g(function () { throw new Error("kaboom"); });
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
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial (c): re-entrancy. The driving generator calls another generator's iterator methods
    // (.next()) between its own yields. Neither resumable frame may corrupt the other.
    [Fact(Timeout = 5000)]
    public async Task GeneratorDrivesAnotherGenerator_FramesStayIndependent()
    {
        await using var engine = CreateEngine();
        // outer is itself resumable (yield step() between yields). step re-enters inner's resumable
        // frame via .next(). Both outer and inner route through the resumable VM; neither frame may
        // corrupt the other across the interleaved suspensions.
        var result = await engine.Evaluate("""
            function* inner() {
                yield 100;
                yield 200;
            }

            function* outer(step) {
                yield step();
                yield step();
            }

            var it = inner();
            var o = outer(function () { return it.next().value; });
            var a = o.next().value;
            var b = o.next().value;
            a + "|" + b;
            """);

        // outer drives inner across its own suspensions: 100 then 200.
        Assert.Equal("100|200", result);
        AssertGeneratorFastPath("outer", argc: 1);
        AssertGeneratorFastPath("inner", argc: 0);
    }

    // End-to-end async: an async function that CALLS a function between awaits routes through the
    // resumable fast path and resolves with the correct value.
    [Fact(Timeout = 5000)]
    public async Task AsyncCallBetweenAwaits_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        // sink(o.a) is a call sitting BETWEEN two await suspension points; the trailing return sink(o.b)
        // is a call after the second await. Both dispatch from resumed frames through the resumable VM.
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(o, sink) {
                await o.first;
                sink(o.a);
                await o.second;
                return sink(o.b);
            }

            var log = [];
            run(
                {
                    first: Promise.resolve(0),
                    second: Promise.resolve(0),
                    a: 4,
                    b: 10
                },
                function (n) { log.push(n); return n * 2; }).then(value => asyncResult = value + "/" + log.join(","));
            asyncResult;
            """);

        // sink(4) logs 4 (between the awaits), sink(10) returns 20 (after the second await).
        Assert.Equal("20/4,10", result);
        AssertAsyncFastPath("run", argc: 2);
    }

    // End-to-end async method: a computed super-member call after await routes through the same resumable
    // helper path and keeps the derived receiver as `this`.
    [Fact(Timeout = 5000)]
    public async Task AsyncSuperMemberCallAfterAwait_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            class Base {
                read(offset) {
                    return this.base + offset;
                }
            }

            class Derived extends Base {
                constructor() {
                    super();
                    this.base = 20;
                }

                async run(gate, name) {
                    await gate;
                    return super[name](3);
                }
            }

            new Derived().run(Promise.resolve(0), "read")
                .then(value => asyncResult = "" + value);
            asyncResult;
            """);

        Assert.Equal("23", result);
        AssertAsyncMethodFastPath(argc: 2);
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

    private void AssertGeneratorMethodFastPath(int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal));

    private void AssertAsyncMethodFastPath(int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal));

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static ExecutionPlan GetClassMethodPlan(string source, string className, string methodName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<ClassDeclaration>(
            pipeline.Analyzed.Body.Single(node =>
                node is ClassDeclaration classDeclaration &&
                classDeclaration.Name.Name == className));
        var method = Assert.Single(declaration.Definition.Members.Where(member => member.Name == methodName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)method.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}

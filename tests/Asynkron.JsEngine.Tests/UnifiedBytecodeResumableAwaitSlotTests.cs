using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for B1 + B44 — binding an AWAITED value into a flat slot / destructuring binding inside a
///     resumable async body, then reading it back across the await suspension
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). `var x = await p` (B1) lowers to
///     `&lt;awaited ops&gt;` -> AwaitValue -> InitializeSlot; `let [a,b] = await p` (B44) lowers to
///     `&lt;awaited ops&gt;` -> AwaitValue -> ApplyDeclarationBindingTarget. AwaitValue suspends the body and
///     pushes the settled value on resume; the store / destructuring runs AFTER the suspension completes, so a
///     later LoadSlot reads the correct value and a rejected promise surfaces as the resumable Throw step.
///
///     Each proof asserts (a) ROUTING — the resumable async fast-path log (a fall-back to the IR runner fails
///     the test) — and (b) the correct runtime result.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableAwaitSlotTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // B1 gate: the awaited-declaration program actually carries AwaitValue + InitializeSlot (proving the
    // slice lowered the await-into-slot shape rather than admitting an unrelated form).
    [Fact]
    public void EvaluateResumable_VarAwait_AdmitsAwaitValueAndInitializeSlot()
    {
        var plan = GetFunctionPlan("""
            async function f(p) { var x = await p; return x + 1; }
            """, "f");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.AwaitValue);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.InitializeSlot);
    }

    // B44 gate: the awaited-destructuring program carries AwaitValue + ApplyDeclarationBindingTarget.
    [Fact]
    public void EvaluateResumable_DestructureAwait_AdmitsAwaitValueAndBindingTarget()
    {
        var plan = GetFunctionPlan("""
            async function f(p) { let [a,b] = await p; return a + b; }
            """, "f");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.AwaitValue);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget);
    }

    // B1 end-to-end: `var x = await p; return x + 1;` resolves to 10 and routes resumable.
    [Fact(Timeout = 5000)]
    public async Task VarAwaitSlot_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) { var x = await p; return x + 1; }
            f(Promise.resolve(9)).then(value => asyncResult = "" + value);
            asyncResult;
            """);

        Assert.Equal("10", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B1 across MULTIPLE statements after the await: the awaited value survives the suspension and is read by
    // several statements that follow.
    [Fact(Timeout = 5000)]
    public async Task VarAwaitSlot_UsedAcrossMultipleStatements_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                var x = await p;
                var y = x * 2;
                var z = y + x;
                return x + "|" + y + "|" + z;
            }
            f(Promise.resolve(5)).then(value => asyncResult = value);
            asyncResult;
            """);

        // x=5, y=10, z=15
        Assert.Equal("5|10|15", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B1 mutate-after-await: the awaited value is bound into a slot across the suspension, then a property of
    // that awaited OBJECT is mutated after the await and read back — proving the store landed in the real flat
    // slot (the object reference survived the suspension), not a transient discarded on resume.
    [Fact(Timeout = 5000)]
    public async Task VarAwaitSlot_MutateAfterAwait_IsVisible()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                var o = await p;
                o.n = o.n + 100;
                return o.n;
            }
            f(Promise.resolve({ n: 1 })).then(value => asyncResult = "" + value);
            asyncResult;
            """);

        Assert.Equal("101", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B1 await binding inside a LOOP: each iteration binds a per-iteration awaited value into the slot and the
    // bound value is consumed (pushed onto a result array) before the next iteration overwrites the slot.
    [Fact(Timeout = 5000)]
    public async Task VarAwaitSlot_InLoop_BindsPerIterationValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(parts) {
                var out = [];
                for (var i = 0; i < 3; i++) {
                    var step = await parts[i];
                    out.push(step);
                }
                return out.join("|");
            }
            f([Promise.resolve("a"), Promise.resolve("b"), Promise.resolve("c")])
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("a|b|c", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B1 MULTIPLE sequential awaits, each into its OWN slot: an earlier awaited slot value must survive a
    // LATER await suspension in the same body (each slot is distinct and all three are read at the end).
    [Fact(Timeout = 5000)]
    public async Task VarAwaitSlot_MultipleSequentialAwaits_EachSlotSurvives()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(a, b, c) {
                let x1 = await a;
                let x2 = await b;
                let x3 = await c;
                return x1 + "|" + x2 + "|" + x3 + "|" + (x1 + x2 + x3);
            }
            f(Promise.resolve(10), Promise.resolve(20), Promise.resolve(30))
                .then(value => asyncResult = value);
            asyncResult;
            """);

        // x1=10 survives two later awaits, x2=20 survives one, x3=30; sum=60.
        Assert.Equal("10|20|30|60", result);
        AssertAsyncFastPath("f", argc: 3);
    }

    // B1 error propagation: a rejected promise surfaces as a throw on the resumed step.
    [Fact(Timeout = 5000)]
    public async Task VarAwaitSlot_RejectedPromise_Throws()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                var x = await p;
                return x + 1;
            }
            f(Promise.reject(new Error("boom")))
                .then(value => asyncResult = "resolved:" + value)
                .catch(err => asyncResult = "caught:" + err.message);
            asyncResult;
            """);

        Assert.Equal("caught:boom", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B44 end-to-end: `let [a,b] = await p` destructures the awaited array and reads both bindings.
    [Fact(Timeout = 5000)]
    public async Task ArrayDestructureAwait_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                let [a, b] = await p;
                return a + "|" + b;
            }
            f(Promise.resolve([3, 4])).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("3|4", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B44 object destructuring across the await, used across multiple following statements.
    [Fact(Timeout = 5000)]
    public async Task ObjectDestructureAwait_UsedAcrossStatements_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                let { x, y } = await p;
                let sum = x + y;
                let diff = x - y;
                return sum + "|" + diff;
            }
            f(Promise.resolve({ x: 10, y: 3 })).then(value => asyncResult = value);
            asyncResult;
            """);

        // sum=13, diff=7
        Assert.Equal("13|7", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B44 mutate-after-await: a destructured OBJECT binding's property is mutated after the await and read
    // back, proving the destructured object reference survived the suspension in its slot.
    [Fact(Timeout = 5000)]
    public async Task ArrayDestructureAwait_MutateAfterAwait_IsVisible()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                let [a, b] = await p;
                a.v = a.v + b.v;
                return a.v;
            }
            f(Promise.resolve([{ v: 5 }, { v: 6 }])).then(value => asyncResult = "" + value);
            asyncResult;
            """);

        Assert.Equal("11", result);
        AssertAsyncFastPath("f", argc: 1);
    }

    // B44 error propagation: a rejected promise surfaces as a throw before destructuring runs.
    [Fact(Timeout = 5000)]
    public async Task ArrayDestructureAwait_RejectedPromise_Throws()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function f(p) {
                let [a, b] = await p;
                return a + b;
            }
            f(Promise.reject(new Error("nope")))
                .then(value => asyncResult = "resolved:" + value)
                .catch(err => asyncResult = "caught:" + err.message);
            asyncResult;
            """);

        Assert.Equal("caught:nope", result);
        AssertAsyncFastPath("f", argc: 1);
    }

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

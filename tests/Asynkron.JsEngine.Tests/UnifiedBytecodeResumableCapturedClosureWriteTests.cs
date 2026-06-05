using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the resumable captured-closure WRITE tier: a generator/async function nested inside an
///     ENCLOSING FUNCTION that mutates that function's local across yield/await. The captured name escapes the
///     resumable activation's own slots, so a captured update (`n++`, `n--`, `++n`, `--n`) lowers to
///     <see cref="UnifiedBytecodeOpCode.UpdateDynamicIdentifier" /> and resolves against the live closure
///     environment threaded onto <see cref="UnifiedBytecodeResumeState.CallingEnvironment" /> (#3108), captured
///     at construction and stable across suspension. The update is ATOMIC inside one resumable step (it never
///     leaves a half-resolved reference on the operand stack across a suspension), so it aliases the SAME
///     enclosing heap slot before and after every suspension. const-safety is enforced by the environment
///     (ResolveIdentifierAssignmentReference -> SetValue throws on a captured `const`), so a captured-const
///     update raises TypeError.
///
///     Captured plain STORE (`n = v`), captured compound STORE (`n += v`), and captured logical compound STORE
///     (`n &&= v`) also route through the same dynamic-reference stack used by the free/global mutation tier.
///     The proof here checks that these forms alias the enclosing heap slot by reading the mutation through a
///     sibling closure after each suspension.
///
///     Each admitted proof asserts (a) ROUTING via the resumable fast-path log and (b) correctness for the
///     adversarial cases: mutate-across-yield, mutation visible in the enclosing slot after suspension,
///     mutate-across-await, captured-const update -> TypeError, and survival across multiple suspensions.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableCapturedClosureWriteTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // Eligibility gate: the captured `n++` admits and the admitted program actually carries the
    // UpdateDynamicIdentifier opcode (proving the captured-WRITE tier expanded, not an unrelated shape).
    [Fact]
    public void EvaluateResumable_CapturedUpdate_AdmitsUpdateDynamicIdentifier()
    {
        var plan = NestedGeneratorPlan("""
            function mk() {
                let n = 0;
                function* g() {
                    n++;
                    yield n;
                }
                return g;
            }
            """, "mk", "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
    }

    // Headline: a generator mutates the captured enclosing `let` (`n++`) across two yields. The update aliases
    // the same enclosing heap slot, so the sequence is 1, 2.
    [Fact(Timeout = 5000)]
    public async Task GeneratorMutatesCapturedLetAcrossYields_AliasesEnclosingSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 0;
                function* g() {
                    n++;
                    yield n;
                    n++;
                    yield n;
                }
                return g;
            }
            var g = mk();
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // The strongest aliasing proof: outer code reads the captured binding (via a sibling `peek`) AFTER the
    // generator mutated it across a suspension. The mutation must be visible in the SAME enclosing slot.
    [Fact(Timeout = 5000)]
    public async Task GeneratorMutationVisibleInEnclosingSlotAfterEachSuspension()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 0;
                function* g() {
                    n++;
                    yield n;
                    n++;
                    yield n;
                }
                function peek() { return n; }
                return { g: g, peek: peek };
            }
            var o = mk();
            var it = o.g();
            it.next();                 // n -> 1
            var afterFirst = o.peek();
            it.next();                 // n -> 2
            var afterSecond = o.peek();
            afterFirst + "|" + afterSecond;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // A captured READ and a captured UPDATE interleaved: the read observes the freshly-updated value because
    // both resolve against the same threaded environment.
    [Fact(Timeout = 5000)]
    public async Task GeneratorInterleavedCapturedReadAndUpdate()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 5;
                function* g() {
                    yield n;       // 5
                    n++;
                    yield n;       // 6
                    --n;
                    yield n;       // 5
                }
                return g;
            }
            var it = mk()();
            var s = "" + it.next().value + it.next().value + it.next().value;
            s;
            """);

        Assert.Equal("565", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Captured-const update must throw TypeError when the mutating step runs — const-safety is enforced by the
    // environment, not by absent const-slot metadata.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCapturedConstUpdate_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                const c = 5;
                function* g() {
                    yield 1;
                    c++;          // TypeError: Assignment to constant variable
                    yield 2;
                }
                return g;
            }
            var it = mk()();
            var first = it.next().value;
            var caught = "none";
            try { it.next(); } catch (e) { caught = e.constructor.name; }
            first + "|" + caught;
            """);

        Assert.Equal("1|TypeError", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Survives many suspensions: each resumed step mutates the SAME enclosing slot.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCapturedUpdateSurvivesMultipleSuspensions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 0;
                function* g() {
                    n++; yield n;
                    n++; yield n;
                    n++; yield n;
                    n++; yield n;
                }
                return g;
            }
            var it = mk()();
            var s = "";
            for (var i = 0; i < 4; i++) { s += it.next().value; }
            s;
            """);

        Assert.Equal("1234", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Async function mutates the captured enclosing local across an await boundary.
    [Fact(Timeout = 5000)]
    public async Task AsyncMutatesCapturedLetAcrossAwait_AliasesEnclosingSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            function mk() {
                let n = 1;
                async function run() {
                    n++;
                    await Promise.resolve(0);
                    n++;
                    return n;
                }
                return run;
            }
            mk()().then(v => done = v);
            done;
            """);

        Assert.Equal(3d, result);
        AssertAsyncFastPath("run", argc: 0);
    }

    // Captured plain assignment aliases the enclosing slot and routes through the resumable VM.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCapturedPlainAssign_RoutesAndAliasesEnclosingSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 10;
                function* g() {
                    n = n + 5;
                    yield n;
                    n = n * 2;
                    yield n;
                }
                function peek() { return n; }
                return { g: g, peek: peek };
            }
            var o = mk();
            var it = o.g();
            var a = it.next().value;
            var afterFirst = o.peek();
            var b = it.next().value;
            var afterSecond = o.peek();
            a + "|" + afterFirst + "|" + b + "|" + afterSecond;
            """);

        Assert.Equal("15|15|30|30", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Captured compound assignment uses the same dynamic reference path and aliases the enclosing slot.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCapturedCompoundAssign_RoutesAndAliasesEnclosingSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 10;
                function* g() {
                    n += 5;
                    yield n;
                    n *= 2;
                    yield n;
                }
                function peek() { return n; }
                return { g: g, peek: peek };
            }
            var o = mk();
            var it = o.g();
            var a = it.next().value;
            var afterFirst = o.peek();
            var b = it.next().value;
            var afterSecond = o.peek();
            a + "|" + afterFirst + "|" + b + "|" + afterSecond;
            """);

        Assert.Equal("15|15|30|30", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Captured logical compound assignment short-circuits and assigns through the enclosing slot reference.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCapturedLogicalCompoundAssign_RoutesAndAliasesEnclosingSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 1;
                let hits = 0;
                function* g() {
                    n ||= (hits = 1);
                    yield n;
                    n &&= 5;
                    yield n;
                }
                function peek() { return n; }
                function hitCount() { return hits; }
                return { g: g, peek: peek, hitCount: hitCount };
            }
            var o = mk();
            var it = o.g();
            var a = it.next().value;
            var afterFirst = o.peek();
            var b = it.next().value;
            var afterSecond = o.peek();
            a + "|" + afterFirst + "|" + b + "|" + afterSecond + "|" + o.hitCount();
            """);

        Assert.Equal("1|1|5|5|0", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Close-finally boundary: a captured update (`n++`) sited INSIDE a finally that protects a yield must
    // keep the generator on the IR runner, because the resumable VM's early-close (.return()/.throw()) path
    // does not re-drive a user finally. Closing the generator early (for-of break) MUST still run the
    // finally, mutating the captured `n` — proven by the sibling `peek`. The generator must NOT route
    // resumable (it would silently drop the finally on close). A non-empty property-write finally is a
    // DIFFERENT, pre-existing tier that keeps routing (ResumableAlreadyRoutingPinTests.B32); only the
    // captured/free UPDATE this change introduced into finally territory is gated here.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCapturedUpdateInFinally_EarlyCloseRunsFinally_StaysOnRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function mk() {
                let n = 0;
                function* g() {
                    try {
                        yield 1;
                        yield 2;
                    } finally {
                        n++;
                    }
                }
                function peek() { return n; }
                return { g: g, peek: peek };
            }
            var o = mk();
            for (var x of o.g()) { break; }   // early close -> finally must run -> n === 1
            o.peek();
            """);

        Assert.Equal(1d, result);
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));
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

    private static ExecutionPlan NestedGeneratorPlan(string source, string outerName, string innerName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var outer = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == outerName));

        FunctionExpression? inner = null;
        foreach (var statement in outer.Function.Body.Statements)
        {
            if (statement is FunctionDeclaration declaration && declaration.Name?.Name == innerName)
            {
                inner = declaration.Function;
            }
        }

        Assert.NotNull(inner);
        var cache = ((IAstCacheable<ExecutionPlanCache>)inner!).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}

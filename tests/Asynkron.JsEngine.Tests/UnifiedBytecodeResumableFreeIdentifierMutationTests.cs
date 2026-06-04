using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the resumable FREE / DYNAMIC IDENTIFIER mutation tier (burn-down B26 / B27 / B28 / B29) —
///     the resumable analogue of the already-admitted sync free-identifier cluster (A14 / A22 / A24). A
///     generator/async whose body WRITES / UPDATES / DELETEs a FREE (module/script-level or captured-outer)
///     identifier across a suspension.
///
///     ADMITTED:
///     <list type="bullet">
///         <item>
///             B27 free UPDATE (<c>freeVar++</c>) lowers to <see cref="UnifiedBytecodeOpCode.UpdateDynamicIdentifier" />
///             — already admitted by the captured-closure tier (commit 71be17015); covered end-to-end here for the
///             FREE (global) case.
///         </item>
///         <item>
///             B28 free DELETE (<c>delete freeVar</c>) lowers to <see cref="UnifiedBytecodeOpCode.DeleteDynamicIdentifier" />.
///             It is SELF-CONTAINED: name + environment + isStrict -> bool, never using the transient
///             dynamicIdentifierReferences array, so there is no pending reference for the resume state to thread.
///             It resolves against the live closure environment on
///             <see cref="UnifiedBytecodeResumeState.CallingEnvironment" /> (#3108, stable across yield/await).
///         </item>
///     </list>
///
///     DECLINED (honest boundary, verified correct on the IR runner, NOT routed):
///     <list type="bullet">
///         <item>
///             B26 free WRITE (<c>freeVar = v</c>) lowers via the
///             <see cref="UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference" /> ->
///             <see cref="UnifiedBytecodeOpCode.StoreDynamicIdentifierReference" /> sequence whose pending
///             AssignmentReference lives in a transient VM-local array, NOT on the resume state. A suspending RHS
///             (<c>freeVar = yield</c>) would lose the store target on resume, so it stays declined.
///         </item>
///         <item>
///             B29 free COMPOUND (<c>freeVar += x</c>) declines even earlier, at the
///             <c>CompoundAssignmentSlotInstruction</c> plan-shape gate, and would carry the same reference-threading
///             hazard as B26.
///         </item>
///     </list>
///
///     Each admitted proof asserts (a) ROUTING via the resumable fast-path log and (b) correctness for the
///     adversarial cases: mutate the SAME global across a suspension (mutate, yield, read after resume ->
///     consistent), and the async variant. Each declined proof asserts the correct value AND the absence of the
///     fast-path log (it ran on the IR runner).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableFreeIdentifierMutationTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // ----- Eligibility gates -----

    // B28: the free `delete gx` admits and the admitted program actually carries the DeleteDynamicIdentifier
    // opcode (proving the free-DELETE tier expanded eligibility, not an unrelated shape).
    [Fact]
    public void EvaluateResumable_FreeDelete_AdmitsDeleteDynamicIdentifier()
    {
        var plan = GetFunctionPlan("""
            function* g() { yield delete gx; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteDynamicIdentifier);
    }

    // B27: the free `c++` admits and carries the UpdateDynamicIdentifier opcode for the FREE (global) case.
    [Fact]
    public void EvaluateResumable_FreeUpdate_AdmitsUpdateDynamicIdentifier()
    {
        var plan = GetFunctionPlan("""
            var c = 5;
            function* g() { yield c++; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
    }

    // B26: the free WRITE declines — its ResolveDynamicIdentifierReference lowering is absent from the
    // resumable opcode allowlist (the pending reference is not threaded on the resume state).
    [Fact]
    public void EvaluateResumable_FreeWrite_DeclinesUnsupportedReferenceLowering()
    {
        var plan = GetFunctionPlan("""
            var s;
            function* g() { s = 9; yield s; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("ResolveDynamicIdentifierReference", result.Reason, StringComparison.Ordinal);
    }

    // B29: the free COMPOUND declines at the CompoundAssignmentSlotInstruction plan-shape gate (earlier than
    // the opcode allowlist).
    [Fact]
    public void EvaluateResumable_FreeCompound_DeclinesAtPlanShapeGate()
    {
        var plan = GetFunctionPlan("""
            var c = 1;
            function* g() { c += 2; yield c; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("CompoundAssignmentSlotInstruction", result.Reason, StringComparison.Ordinal);
    }

    // ----- B28 free DELETE: end-to-end routing + correctness -----

    // Headline: a generator deletes a free global property and yields the boolean delete result. Routes
    // resumable and returns true (the property was configurable).
    [Fact(Timeout = 5000)]
    public async Task GeneratorDeletesFreeGlobal_RoutesResumableAndReturnsTrue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            globalThis.gx = 1;
            function* g() { yield delete gx; }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(true, result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: the delete must hit the SAME global across a suspension. Read it (live) before the yield,
    // delete it after resuming, then confirm `typeof gx === "undefined"` from outer code. The mutation is
    // visible on the SAME global object the outer scope sees.
    [Fact(Timeout = 5000)]
    public async Task GeneratorDeletesFreeGlobalAcrossSuspension_MutatesSameGlobal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            globalThis.gx = 7;
            function* g() {
                yield gx;          // live read of the free global: 7
                yield delete gx;   // delete the SAME global after resuming
            }
            var it = g();
            var before = it.next().value;       // 7
            var deleted = it.next().value;       // true
            var after = (typeof gx);             // outer code observes the deletion
            before + "|" + deleted + "|" + after;
            """);

        Assert.Equal("7|true|undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: outer code re-creates the global WHILE the generator is suspended; the resumed delete step
    // must observe and delete the CURRENT binding (resolution is live, not snapshotted at construction).
    [Fact(Timeout = 5000)]
    public async Task GeneratorDeleteAfterOuterRecreatesGlobal_DeletesLiveBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            globalThis.gx = 1;
            function* g() {
                yield 0;
                yield delete gx;   // resolved live on resume
            }
            var it = g();
            it.next();
            delete gx;             // outer removes it
            globalThis.gx = 42;    // outer re-creates it while suspended
            var deleted = it.next().value;  // resumed step deletes the live (re-created) binding
            deleted + "|" + (typeof gx);
            """);

        Assert.Equal("true|undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Deleting an absent free identifier returns true per spec (no binding to remove), and still routes.
    [Fact(Timeout = 5000)]
    public async Task GeneratorDeletesAbsentFreeIdentifier_ReturnsTrueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() { yield delete neverDefined; }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(true, result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Async variant: an async function deletes a free global across an await boundary.
    [Fact(Timeout = 5000)]
    public async Task AsyncDeletesFreeGlobalAcrossAwait_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            globalThis.gx = 5;
            async function run() {
                await Promise.resolve(0);
                return delete gx;
            }
            run().then(v => asyncResult = v + "|" + (typeof gx));
            asyncResult;
            """);

        Assert.Equal("true|undefined", result);
        AssertAsyncFastPath("run", argc: 0);
    }

    // ----- B27 free UPDATE: end-to-end routing + correctness for the FREE (global) case -----

    // Headline from the burn-down brief: `var c=5; function* g(){ yield c++; }` -> 5, c===6, routed.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeUpdate_PostfixYieldsOldValueAndMutatesGlobal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var c = 5;
            function* g() { yield c++; }
            var it = g();
            var yielded = it.next().value;
            yielded + "|" + c;
            """);

        Assert.Equal("5|6", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: the update must hit the SAME global across a suspension. Mutate, yield, read after resume.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeUpdateAcrossSuspension_MutatesSameGlobal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var c = 0;
            function* g() {
                c++;
                yield c;   // 1
                c++;
                yield c;   // 2 — same global mutated across the suspension
            }
            var it = g();
            var a = it.next().value;
            var mid = c;            // outer observes the mutation while suspended
            var b = it.next().value;
            a + "|" + mid + "|" + b + "|" + c;
            """);

        Assert.Equal("1|1|2|2", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Async variant: an async function updates a free global across an await boundary.
    [Fact(Timeout = 5000)]
    public async Task AsyncFreeUpdateAcrossAwait_MutatesSameGlobal()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var c = 10;
            async function run() {
                c++;
                await Promise.resolve(0);
                c++;
                return c;
            }
            run().then(v => asyncResult = v + "|" + c);
            asyncResult;
            """);

        Assert.Equal("12|12", result);
        AssertAsyncFastPath("run", argc: 0);
    }

    // ----- B26 / B29 honest decline pins: correct value, NOT routed (ran on the IR runner) -----

    // B26 free WRITE across a suspension: the value is correct, but the generator does NOT route resumable.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeWrite_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var s = 0;
            function* g() {
                s = 9;
                yield s;
                s = s + 1;
                yield s;
            }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b + "|" + s;
            """);

        Assert.Equal("9|10|10", result);
        AssertNotResumableRouted();
    }

    // B29 free COMPOUND across a suspension: correct value, NOT routed.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeCompound_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var c = 1;
            function* g() {
                c += 2;
                yield c;
                c *= 3;
                yield c;
            }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b + "|" + c;
            """);

        Assert.Equal("3|9|9", result);
        AssertNotResumableRouted();
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

    private void AssertNotResumableRouted()
    {
        var snapshot = CurrentLogger!.Collector.Snapshot();
        Assert.DoesNotContain(
            snapshot,
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(
            snapshot,
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal));
    }

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

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
///             B26 free WRITE (<c>freeVar = v</c>) lowers through the pre-resolved
///             <see cref="UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference" /> ->
///             <see cref="UnifiedBytecodeOpCode.StoreDynamicIdentifierReference" /> sequence. The pending
///             AssignmentReference is persisted on <see cref="UnifiedBytecodeResumeState" />, so a suspending RHS
///             (<c>freeVar = yield</c>) resumes with the exact target selected before suspension.
///         </item>
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
///         <item>
///             B29 free COMPOUND / LOGICAL COMPOUND (<c>freeVar += x</c>, <c>freeVar &&= x</c>)
///             lower through the same pending reference stack. The compiler emits
///             <see cref="UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference" /> ->
///             <see cref="UnifiedBytecodeOpCode.LoadDynamicIdentifierReference" />, then the RHS / branch
///             sequence, then <see cref="UnifiedBytecodeOpCode.StoreDynamicIdentifierReference" /> or
///             <see cref="UnifiedBytecodeOpCode.PopDynamicIdentifierReference" /> for short-circuited logical
///             assignments.
///         </item>
///     </list>
///
///     Each admitted proof asserts (a) ROUTING via the resumable fast-path log and (b) correctness for the
///     adversarial cases: mutate the SAME global across a suspension (mutate, yield, read after resume ->
///     consistent), and the async variant.
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

    // B26: the free WRITE admits and carries the pre-resolved dynamic reference store sequence.
    [Fact]
    public void EvaluateResumable_FreeWrite_AdmitsDynamicReferenceStore()
    {
        var plan = GetFunctionPlan("""
            var s;
            function* g() { s = 9; yield s; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
    }

    // B29: the free COMPOUND admits and carries the pre-resolved dynamic reference read-modify-write sequence.
    [Fact]
    public void EvaluateResumable_FreeCompound_AdmitsDynamicReferenceReadModifyWrite()
    {
        var plan = GetFunctionPlan("""
            var c = 1;
            function* g() { c += 2; yield c; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifierReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
    }

    // B29: a free LOGICAL COMPOUND admits and cleans up the pending reference on the short-circuit branch.
    [Fact]
    public void EvaluateResumable_FreeLogicalCompound_AdmitsDynamicReferenceCleanup()
    {
        var plan = GetFunctionPlan("""
            var c = 1;
            function* g() { c ||= 2; yield c; }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadDynamicIdentifierReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PopDynamicIdentifierReference);
    }

    // B29 does not widen the known B32 early-close finally limitation. Dynamic mutations inside finally stay on
    // the IR runner until the resumable VM owns finally execution for .return() / .throw().
    [Fact]
    public void EvaluateResumable_FreeCompoundInFinally_DeclinesForEarlyCloseCleanup()
    {
        var plan = GetFunctionPlan("""
            var c = 0;
            function* g() {
                try { yield 1; }
                finally { c += 2; }
            }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("dynamic mutation", result.Reason, StringComparison.Ordinal);
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

    // ----- B26 free WRITE: end-to-end routing + correctness -----

    // B26 free WRITE across a suspension: the value is correct and the generator routes resumable.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeWrite_RoutesResumableAndMutatesGlobal()
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
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial B26 proof: the LHS reference is resolved BEFORE evaluating the RHS. The RHS suspends, outer
    // code changes a same-named binding while suspended, and the resumed store still writes through the pending
    // AssignmentReference carried by UnifiedBytecodeResumeState.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeWriteWithSuspendingRhs_RoutesAndPreservesPendingReference()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var s = 0;
            function* g() {
                s = yield "send";
                yield s;
            }
            var it = g();
            var first = it.next().value;
            s = 41;
            var second = it.next(9).value;
            first + "|" + second + "|" + s;
            """);

        Assert.Equal("send|9|9", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Async variant: a free write after an await routes and mutates the same outer binding.
    [Fact(Timeout = 5000)]
    public async Task AsyncFreeWriteAcrossAwait_RoutesResumableAndMutatesGlobal()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var s = 1;
            async function run() {
                await Promise.resolve(0);
                s = 5;
                return s;
            }
            run().then(v => asyncResult = v + "|" + s);
            asyncResult;
            """);

        Assert.Equal("5|5", result);
        AssertAsyncFastPath("run", argc: 0);
    }

    // ----- B29 free COMPOUND / LOGICAL COMPOUND: end-to-end routing + correctness -----

    // B29 free COMPOUND across a suspension: correct value and routed.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeCompound_RoutesResumableAndMutatesGlobal()
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
        AssertGeneratorFastPath("g", argc: 0);
    }

    // B29 logical assignment: short-circuit skips the RHS and assignment branch; assignment branch mutates.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeLogicalCompound_RoutesAndPreservesShortCircuit()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var c = 1;
            var hits = 0;
            function* g() {
                c ||= (hits = 1);
                yield c;
                c &&= 5;
                yield c;
            }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b + "|" + hits + "|" + c;
            """);

        Assert.Equal("1|5|0|5", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Async variant: a free compound assignment after an await routes and mutates the same outer binding.
    [Fact(Timeout = 5000)]
    public async Task AsyncFreeCompoundAcrossAwait_RoutesResumableAndMutatesGlobal()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var c = 10;
            async function run() {
                await Promise.resolve(0);
                c += 5;
                return c;
            }
            run().then(v => asyncResult = v + "|" + c);
            asyncResult;
            """);

        Assert.Equal("15|15", result);
        AssertAsyncFastPath("run", argc: 0);
    }

    // Guard proof: early-close finally cleanup with a newly-admitted dynamic compound stays on the IR runner.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreeCompoundInFinally_EarlyReturnRunsCleanupOnRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var c = 0;
            function* g() {
                try {
                    yield 1;
                } finally {
                    c += 2;
                }
            }
            var it = g();
            var first = it.next().value;
            it.return();
            first + "|" + c;
            """);

        Assert.Equal("1|2", result);
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

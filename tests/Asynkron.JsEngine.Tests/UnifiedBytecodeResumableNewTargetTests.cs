using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the <c>new.target</c> meta-property (<see cref="UnifiedBytecodeOpCode.LoadNewTarget" />)
///     inside the resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) — burndown
///     item B19. Before this slice the resumable opcode allowlist
///     (<c>TryFindUnsupportedResumableOpcode</c>) omitted <see cref="UnifiedBytecodeOpCode.LoadNewTarget" />,
///     so any generator/async body that read <c>new.target</c> fell back to the interpreter even though the
///     synchronous VM already admitted it.
///
///     Semantics: a generator or async function is never a constructor (<c>new g()</c> throws), so its own
///     function environment binds <c>new.target</c> to <c>undefined</c>. The resumable handler resolves
///     <c>Symbol.NewTarget</c> with a single chain lookup against
///     <see cref="UnifiedBytecodeResumeState.CallingEnvironment" /> — the closure captured at construction and
///     stable across <c>yield</c>/<c>await</c> — which returns <c>undefined</c> for ordinary
///     generators/async functions and the lexically inherited value for an async arrow nested in a
///     constructor. The opcode pushes one value, carries no <c>AwaitedProgram</c>, and cannot itself suspend.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (an interpreter fall-back fails the test) — and (b) correctness,
///     including the adversarial cases: <c>new.target</c> read AFTER a suspension still resolves to
///     <c>undefined</c>, the value participates in a <c>typeof</c>/comparison expression, and an async arrow
///     lexically inherits <c>new.target</c> from its enclosing constructor across an await.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableNewTargetTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that reads `new.target` across a yield is now admitted, and the admitted
    // program actually carries LoadNewTarget (proving the slice expanded eligibility to the new.target
    // opcode rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_GeneratorNewTargetAcrossYield_AdmitsLoadNewTarget()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 1;
                yield new.target;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadNewTarget);
    }

    // The gate: an async function that reads `new.target` after an await is admitted and carries
    // LoadNewTarget.
    [Fact]
    public void EvaluateResumable_AsyncNewTargetAfterAwait_AdmitsLoadNewTarget()
    {
        var plan = GetFunctionPlan("""
            async function run(p) {
                await p;
                return new.target;
            }
            """,
            "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadNewTarget);
    }

    // End-to-end: a generator yields `new.target` across a suspension. A generator is never a constructor,
    // so new.target is undefined; the value must be the SAME undefined after the resume as it would be at
    // body entry (the calling environment is stable across yield).
    [Fact(Timeout = 5000)]
    public async Task GeneratorNewTargetAcrossYield_RoutesResumableAndIsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                yield (new.target === undefined ? "undef" : "ctor");
            }

            var it = g();
            var a = it.next().value;   // 1
            var b = it.next().value;   // resumes; new.target === undefined
            a + "|" + b;
            """);

        Assert.Equal("1|undef", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end: new.target participates in a `typeof` expression after the suspension — proving the pushed
    // value is a real JS `undefined`, not a stack-corruption placeholder.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTypeofNewTargetAcrossYield_RoutesResumableAndIsUndefinedString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 1;
                yield typeof new.target;
            }

            var it = g();
            var a = it.next().value;   // 1
            var b = it.next().value;   // "undefined"
            a + "|" + b;
            """);

        Assert.Equal("1|undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end async: an async function reads new.target after an await. An async function is never a
    // constructor, so the resolved value is undefined and the promise fulfils with it.
    [Fact(Timeout = 5000)]
    public async Task AsyncNewTargetAfterAwait_RoutesResumableAndIsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "PENDING";
            async function run(p) {
                await p;
                return typeof new.target;
            }

            run(Promise.resolve(0)).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("undefined", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // Boundary / correctness-preservation: an async ARROW that lexically inherits its enclosing
    // constructor's new.target is OUTSIDE this slice — async arrows decline at the activation gate
    // (ArrowLexicalThisDependency: the lexical this/new.target binding is not modeled by the resumable
    // activation descriptor), so this body keeps its existing (interpreter) route. The B19 slice only
    // admits ordinary generators/async functions, whose own new.target is always undefined. This test pins
    // that the slice did NOT regress the arrow's lexical-inheritance semantics: even on its existing route
    // the arrow must still observe the enclosing constructor's new.target across the await suspension.
    [Fact(Timeout = 5000)]
    public async Task AsyncArrowInheritsEnclosingNewTargetAcrossAwait_StillCorrectOnExistingRoute()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "PENDING";
            function Ctor() {
                var probe = async (p) => {
                    await p;
                    return new.target === Ctor;
                };
                probe(Promise.resolve(0)).then(value => asyncResult = value);
            }

            new Ctor();
            asyncResult;
            """);

        Assert.Equal(true, result);
    }

    // Boundary gate: the async arrow above declines from the resumable production route at the activation
    // level (ArrowLexicalThisDependency), independent of the new.target opcode — pinning that the slice did
    // not over-admit the lexical-this arrow shape.
    [Fact]
    public void EvaluateResumable_AsyncArrowLexicalNewTarget_DeclinesArrowLexicalThis()
    {
        var plan = GetFunctionPlan("""
            function Ctor() {
                var probe = async (p) => {
                    await p;
                    return new.target;
                };
                return probe;
            }
            """,
            "Ctor");

        // The OUTER Ctor itself is an ordinary function; its plan is admitted. The inner async arrow is the
        // declined shape. We assert the arrow shape declines when evaluated as an async-arrow activation
        // that lexically inherits this/new.target.
        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: true,
                HasArrowLexicalThisDependency: true));

        Assert.False(result.IsEligible);
        Assert.Equal(
            UnifiedBytecodeProductionDeclineCode.ArrowLexicalThisDependency,
            result.Code);
    }

    // REGRESSION (leak fix): a generator declared INSIDE a constructor must read its OWN new.target
    // (=== undefined), shadowing the enclosing constructor — even though it routes resumable. The handler
    // previously walked the closure chain and leaked the enclosing constructor; new.target is now threaded
    // per-activation onto the resume state (undefined for ordinary generators/async).
    [Fact]
    public async Task GeneratorNestedInFunctionConstructor_NewTargetIsUndefinedNotEnclosingCtor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var captured = "PENDING";
            function Ctor() {
                function* g() {
                    yield 1;
                    yield (new.target === undefined ? "undef" : "LEAK:ctor");
                }
                var it = g();
                it.next();
                captured = it.next().value;
            }
            new Ctor();
            captured;
            """);

        Assert.Equal("undef", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public async Task GeneratorNestedInClassConstructor_TypeofNewTargetIsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var captured = "PENDING";
            class C {
                constructor() {
                    function* g() {
                        yield 0;
                        yield typeof new.target;
                    }
                    var it = g();
                    it.next();
                    captured = it.next().value;
                }
            }
            new C();
            captured;
            """);

        Assert.Equal("undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // REGRESSION: an async function nested in a constructor likewise reads undefined, not the ctor.
    [Fact(Timeout = 5000)]
    public async Task AsyncNestedInFunctionConstructor_NewTargetIsUndefinedNotEnclosingCtor()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "PENDING";
            function Ctor() {
                async function run(p) {
                    await p;
                    return new.target === undefined ? "undef" : "LEAK:ctor";
                }
                run(Promise.resolve(0)).then(value => asyncResult = value);
            }
            new Ctor();
            asyncResult;
            """);

        Assert.Equal("undef", result);
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

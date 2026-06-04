using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for <c>typeof freeVar</c> of a free / dynamic identifier inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />, item B25). A generator/async whose
///     body queries <c>typeof</c> of an identifier that escapes this activation's slots (a module/script-
///     level binding, an unbound free name, or a captured outer binding) is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />. The query lowers to
///     <see cref="UnifiedBytecodeOpCode.TypeOfDynamicIdentifier" /> and resolves against the live closure
///     environment threaded onto <see cref="UnifiedBytecodeResumeState.CallingEnvironment" /> (#3108) — the
///     same env the already-admitted free dynamic READS / CALL targets use, stable across yield/await.
///
///     The defining property under test: <c>typeof</c> NEVER throws ReferenceError. <c>typeof unbound</c>
///     yields <c>"undefined"</c> (no throw) even though a bare READ of <c>unbound</c> would throw; a bound
///     free name yields its runtime type. Each proof asserts (a) ROUTING — eligibility via EvaluateResumable
///     plus the resumable fast-path log for the end-to-end run (an interpreter fall-back fails the test) —
///     and (b) correctness across the skeptic's edges: the query straddles a yield/await, the binding is
///     mutated/rebound while the frame is suspended (live read, not a snapshot), and the generator is nested
///     inside a constructor (the per-activation environment must still resolve the free name).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableTypeOfDynamicTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that queries `typeof` of a free/dynamic identifier across a yield is now
    // admitted, and the admitted program actually carries the TypeOfDynamicIdentifier opcode (proving the
    // slice expanded eligibility to the free-typeof shape rather than admitting an unrelated opcode).
    [Fact]
    public void EvaluateResumable_TypeOfFreeIdentifierBetweenYields_AdmitsTypeOfDynamicOpcode()
    {
        var plan = GetFunctionPlan("""
            var defined = 5;
            function* g() {
                yield typeof defined;
                yield typeof undeclaredFree;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.TypeOfDynamicIdentifier);
    }

    // End-to-end generator: typeof of a DEFINED free binding and typeof of an UNDECLARED free name both
    // straddle a yield. The defined binding yields its type; the unbound name yields "undefined" with NO
    // ReferenceError (the core typeof invariant). Routing is asserted via the generator fast-path log.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTypeOfDefinedAndUndeclaredFree_RoutesResumableAndProducesTypes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var defined = 5;
            function* g() {
                yield typeof defined;          // "number"
                yield typeof undeclaredFree;   // "undefined" (unbound, NO throw)
            }

            var it = g();
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("number|undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Skeptic concern #1: typeof must observe the LIVE binding after a resume, not a value snapshot taken at
    // construction. The defined global `box` is reassigned to a different-typed value while the generator is
    // suspended; the resumed `typeof box` step must report the new type.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTypeOfGlobalRetypedBetweenYields_ResumedStepSeesNewType()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = 1;
            function* g() {
                yield typeof box;   // "number"
                yield typeof box;   // "string" after the outer retype
            }

            var it = g();
            var first = it.next().value;
            box = "hello";          // retype while suspended at the first yield
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("number|string", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Skeptic concern #2: a free name that is UNBOUND at the first step but later BOUND by outer code must
    // flip from "undefined" to its real type across the resume — confirming the query runs against the live
    // env each step and that the unbound case genuinely takes the no-throw "undefined" branch.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTypeOfFreeNameBoundBetweenYields_FlipsFromUndefinedToType()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield typeof lateGlobal;   // "undefined" (not yet a global)
                yield typeof lateGlobal;   // "function" after outer code defines it
            }

            var it = g();
            var first = it.next().value;
            lateGlobal = function () {};   // create the global binding while suspended
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("undefined|function", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a generator NESTED INSIDE A CONSTRUCTOR queries typeof of a free/global binding across a
    // yield. The free name (`registry`) is module/script-level — it does NOT live in the constructor
    // activation's slots — so it must resolve through the generator's OWN threaded calling environment, not
    // be confused with any per-activation (this/new.target) value. typeof of the unbound `nope` still yields
    // "undefined" with no throw. This guards the per-activation-vs-free distinction the burn-down flags.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedInConstructorTypeOfFree_RoutesResumableAndResolvesFree()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var registry = "tag";
            function Box() {
                this.gen = function* makeGen() {
                    yield typeof registry;   // free/global "string"
                    yield typeof nope;       // unbound -> "undefined", no throw
                };
            }

            var b = new Box();
            var it = b.gen();
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("string|undefined", result);
        AssertGeneratorFastPath("makeGen", argc: 0);
    }

    // End-to-end async: an async body queries typeof of a defined free binding and an undeclared free name
    // across an await suspension point. Both queries dispatch from a resumed frame; the undeclared name
    // resolves to "undefined" with no rejection. Routing asserted via the async fast-path log.
    [Fact(Timeout = 5000)]
    public async Task AsyncTypeOfFreeAcrossAwait_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var defined = 7;
            async function run(o) {
                await o.gate;
                return typeof defined + "|" + typeof undeclaredAsyncFree;
            }

            run({ gate: Promise.resolve(0) })
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("number|undefined", result);
        AssertAsyncFastPath("run", argc: 1);
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

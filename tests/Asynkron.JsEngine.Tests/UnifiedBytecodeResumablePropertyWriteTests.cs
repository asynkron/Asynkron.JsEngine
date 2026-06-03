using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for property WRITES / UPDATES / DELETES inside a resumable body (burn-down B1) on the
///     resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async
///     whose body assigns (<c>o.x = v</c>, <c>o[k] = v</c>), updates (<c>o.x++</c>, <c>o.x += n</c>,
///     <c>o.x ||= n</c>) or deletes (<c>delete o.x</c>, <c>delete o[k]</c>) a property is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />. These lower to the existing
///     <see cref="UnifiedBytecodeOpCode.SetNamedProperty" />/<see cref="UnifiedBytecodeOpCode.SetComputedProperty" />,
///     <see cref="UnifiedBytecodeOpCode.UpdateNamedProperty" />/<see cref="UnifiedBytecodeOpCode.UpdateComputedProperty" />,
///     the compound-set read halves (<see cref="UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet" />/
///     <see cref="UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet" />) and
///     <see cref="UnifiedBytecodeOpCode.DeleteNamedProperty" />/<see cref="UnifiedBytecodeOpCode.DeleteComputedProperty" />
///     opcodes, which now have <c>ExecuteResumable</c> handlers mirroring the synchronous VM and are on the
///     resumable opcode allowlist. Strict mode is threaded onto
///     <see cref="UnifiedBytecodeResumeState.IsStrict" /> so strict-mode write/delete faults throw correctly.
///
///     Each proof asserts (a) ROUTING — eligibility via EvaluateResumable plus the resumable fast-path log
///     for the end-to-end run (a fall-back to the interpreter fails the test) — and (b) correctness for the
///     adversarial cases: a property mutated across suspension, the operand-stack value the assignment
///     expression evaluates to, computed-key forms, strict-mode faults throwing, and the boundary
///     (private-member and super-property mutation still decline).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumablePropertyWriteTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that WRITES, UPDATES and DELETES properties between yields is now admitted,
    // and the admitted program actually carries the property-mutation opcodes (proving the slice expanded
    // eligibility to the property-write tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_NamedWriteUpdateAndDelete_AdmitsPropertyMutationOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                o.x = 1;
                yield o.x;
                o.x++;
                yield o.x;
                delete o.x;
                yield ("x" in o);
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
    }

    [Fact]
    public void EvaluateResumable_ComputedWriteUpdateAndDelete_AdmitsComputedMutationOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(o, k) {
                o[k] = 1;
                yield o[k];
                o[k] += 5;
                yield o[k];
                delete o[k];
                yield (k in o);
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    // End-to-end generator: named write, update (++), compound (+=) across yields produce the correct
    // value sequence AND leave the correct final state on the receiver, routed through the resumable VM.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNamedWriteUpdateCompound_RoutesResumableAndMutates()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                o.x = 1;        // write; expression evaluates to 1
                yield o.x;
                o.x++;          // postfix update; yields 2 after
                yield o.x;
                o.x += 10;      // compound; yields 12
                yield o.x;
            }
            var o = { x: 0 };
            var it = g(o);
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            a + "|" + b + "|" + c + "|" + o.x;
            """);

        Assert.Equal("1|2|12|12", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // The assignment EXPRESSION's value (not the property read-back) is what lands on the operand stack:
    // `(o.x = 5)` evaluates to 5 even though a setter could store something else. Proves SetNamedProperty
    // replaces the operand stack top with the assigned value, surviving the yield.
    [Fact(Timeout = 5000)]
    public async Task GeneratorAssignmentExpressionValue_IsAssignedValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield (o.x = 5);
            }
            var sink = [];
            var o = {};
            Object.defineProperty(o, "x", { set: function (v) { sink.push(v + 100); } });
            var produced = g(o).next().value;
            produced + "|" + sink.join(",");
            """);

        // The setter ran (sink = 105) but the assignment expression still evaluates to the RHS (5).
        Assert.Equal("5|105", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Computed forms end-to-end: write, compound and delete with a dynamic key across yields.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedWriteCompoundDelete_RoutesResumableAndMutates()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                o[k] = 2;
                yield o[k];
                o[k] += 3;
                yield o[k];
                delete o[k];
                yield (k in o);
            }
            var o = {};
            var it = g(o, "p");
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            a + "|" + b + "|" + c;
            """);

        // 2, then 5, then `false` after the delete.
        Assert.Equal("2|5|false", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // Logical assignment (`o.x ||= n`) reuses the named compound-set read half. The LHS is truthy so the
    // RHS is not stored; the read-back value is yielded.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNamedLogicalAssignment_ShortCircuitsAndYieldsExisting()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                o.x ||= 99;   // o.x is 7 (truthy), so 99 is NOT stored
                yield o.x;
                o.y ||= 42;   // o.y is undefined, so 42 IS stored
                yield o.y;
            }
            var o = { x: 7 };
            var it = g(o);
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("7|42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial: a property the generator writes is observed live by outer code WHILE the generator is
    // suspended, proving the write went through the shared receiver, not a copy.
    [Fact(Timeout = 5000)]
    public async Task GeneratorWriteVisibleToOuterCodeWhileSuspended_MutatesSharedReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                o.count = 1;
                yield;            // suspend with o.count = 1 visible to outer code
                o.count += 1;
                yield;
            }
            var o = { count: 0 };
            var it = g(o);
            it.next();
            var seenAfterFirst = o.count;   // 1
            it.next();
            var seenAfterSecond = o.count;  // 2
            seenAfterFirst + "|" + seenAfterSecond;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Strict-mode fault: writing a non-writable property of a frozen object inside a STRICT generator must
    // throw a TypeError when the step that performs the write runs. Proves IsStrict is threaded onto the
    // resume state and consulted by the resumable SetNamedProperty handler.
    [Fact(Timeout = 5000)]
    public async Task StrictGeneratorWriteToFrozenObject_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";
            function* g(o) {
                o.x = 9;   // throws in strict mode (frozen receiver)
                yield 1;
            }
            var o = Object.freeze({});
            var caught = "none";
            try {
                g(o).next();
            } catch (e) {
                caught = e.constructor.name;
            }
            caught;
            """);

        Assert.Equal("TypeError", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Sloppy-mode counterpart: writing a NON-WRITABLE existing property is a SILENT no-op (no throw) and
    // leaves the original value in place. Proves the strict flag is not hard-coded true in the resumable
    // handler — the same write throws in the strict test above.
    [Fact(Timeout = 5000)]
    public async Task SloppyGeneratorWriteToNonWritableProperty_SilentlyIgnored()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                o.x = 9;   // silent no-op in sloppy mode (x is non-writable)
                yield o.x;
            }
            var o = {};
            Object.defineProperty(o, "x", { value: 1, writable: false, configurable: true });
            g(o).next().value;
            """);

        // The write was ignored; the original value 1 remains.
        Assert.Equal(1d, result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Async end-to-end: a write before an await and an update after it; the resolved value reflects both.
    [Fact(Timeout = 5000)]
    public async Task AsyncWriteThenUpdateAcrossAwait_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(o) {
                o.x = 41;          // write before suspension
                await o.ready;
                o.x++;             // update after suspension
                return o.x;
            }

            run({ ready: Promise.resolve(0) })
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(42d, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // Boundary #1: a TRUE private-member write (`this.#x = v`) inside a generator still declines — the
    // private opcodes are not on the resumable allowlist, so the slice did not over-admit private members.
    [Fact]
    public void EvaluateResumable_PrivateFieldWrite_StillDeclines()
    {
        var plan = GetGeneratorMethodPlan("""
            class C {
                #x = 0;
                *g() { this.#x = 5; yield this.#x; }
            }
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency, result.Code);
    }

    // Boundary #2: a super-property write (`super.x = v`) inside a generator method still declines — the
    // SetNamedSuperProperty opcode is not on the resumable allowlist.
    [Fact]
    public void EvaluateResumable_SuperPropertyWrite_StillDeclines()
    {
        var plan = GetGeneratorMethodPlan("""
            class B { }
            class C extends B {
                *g() { super.x = 5; yield super.x; }
            }
            """);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
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

    // Resolves the plan for the generator METHOD `*g()` of the last class declaration in the source. Used
    // for the private/super boundary proofs where the body must live inside a class.
    private static ExecutionPlan GetGeneratorMethodPlan(string source)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var classDeclaration = pipeline.Analyzed.Body.OfType<ClassDeclaration>().Last();
        var method = classDeclaration.Definition.Members
            .Single(member => member.Function.IsGenerator);
        var cache = ((IAstCacheable<ExecutionPlanCache>)method.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}

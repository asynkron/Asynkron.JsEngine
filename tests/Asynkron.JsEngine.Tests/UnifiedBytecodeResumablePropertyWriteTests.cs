using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for property WRITES (<c>o.x = v</c>, <c>o[k] = v</c>, <c>this.x = v</c>) inside the
///     resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async
///     whose body assigns to a property is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />; before this slice the
///     resumable opcode allowlist (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.SetNamedProperty" /> /
///     <see cref="UnifiedBytecodeOpCode.SetComputedProperty" />, so any property write fell back to the
///     interpreter.
///
///     The assignment value can itself suspend (<c>o.x = yield 1</c>); the base — and, for the computed
///     form, the key — sit on the operand stack across the suspension and are restored on resume because
///     <see cref="UnifiedBytecodeResumeState.OperandStack" /> is the resume state's stable backing store,
///     the same mechanism the already-admitted property READS rely on. The resumable handlers reuse the
///     sync VM's <c>SetPropertyValue</c> helper, which ORs <c>context.CurrentScope.IsStrict</c>, so strict
///     semantics (a write to a read-only property throwing) are preserved.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (a fall-back to the interpreter fails the test) — and (b)
///     correctness for the adversarial cases: a write whose value crosses a suspension, a computed-key
///     write, a <c>this</c> write, a strict read-only write still throwing, and an async write across an
///     await. The NEGATIVE proof pins that property UPDATES (<c>o.x++</c>) stay declined.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumablePropertyWriteTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that WRITES a named property whose value crosses a yield is now admitted, and
    // the admitted program actually carries SetNamedProperty (proving the slice expanded eligibility to
    // the property-write tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_NamedPropertyWriteAcrossYield_AdmitsSetNamedProperty()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                o.x = yield 1;
                yield o.x;
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
    }

    // The gate: a computed-key write (`o[k] = ...`) across a yield is admitted and carries
    // SetComputedProperty.
    [Fact]
    public void EvaluateResumable_ComputedPropertyWriteAcrossYield_AdmitsSetComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function* g(o, k) {
                o[k] = yield 1;
                yield o[k];
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetComputedProperty);
    }

    // A dynamic FREE/captured-variable UPDATE (`freeGlobal++`) between yields is now ADMITTED: it lowers to
    // UpdateDynamicIdentifier, which resolves the name against the threaded CallingEnvironment (stable across
    // suspension) and is on the resumable opcode allowlist with a matching ExecuteResumable handler. The
    // update is atomic inside one resumable step (it never leaves a half-resolved reference on the operand
    // stack across a suspension), and const-safety is enforced by the environment. See
    // UnifiedBytecodeResumableCapturedClosureWriteTests for the captured-enclosing-local end-to-end battery.
    // Plain dynamic stores are covered by UnifiedBytecodeResumableFreeIdentifierMutationTests; compound stores
    // remain a separate B29 boundary.
    [Fact]
    public void EvaluateResumable_DynamicFreeVariableUpdate_AdmitsUpdateDynamicIdentifier()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 1;
                freeGlobal++;
                yield 2;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateDynamicIdentifier);
    }

    // B26 gate: the dynamic plain STORE `freeGlobal = v` now admits through the pre-resolved dynamic
    // assignment-reference sequence. The suspending-RHS runtime proof lives in
    // UnifiedBytecodeResumableFreeIdentifierMutationTests.
    [Fact]
    public void EvaluateResumable_DynamicFreeVariableStore_AdmitsDynamicReferenceStore()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 1;
                freeGlobal = 7;
                yield 2;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.StoreDynamicIdentifierReference);
    }

    [Fact]
    public void EvaluateResumable_NamedSuperPropertyWriteAcrossYield_AdmitsSetNamedSuperProperty()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                set value(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                *g() {
                    super.value = yield 1;
                    yield this.seen;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedSuperProperty);
    }

    [Fact]
    public void EvaluateResumable_ComputedSuperPropertyWriteAcrossYield_AdmitsSetComputedSuperProperty()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                set dynamic(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                *g(key) {
                    super[key] = yield 1;
                    yield this.seen;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetComputedSuperProperty);
    }

    // End-to-end: a named write whose value crosses a yield routes through the resumable fast path,
    // produces the correct value sequence, AND the mutation persists on the object after the generator
    // finishes (proving the write hit the real object, not a stack copy).
    [Fact(Timeout = 5000)]
    public async Task GeneratorNamedWriteAcrossYield_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                o.x = yield 1;
                yield o.x;
            }

            var obj = {};
            var it = g(obj);
            var a = it.next().value;     // yields 1
            var b = it.next(42).value;   // resumes with 42 -> o.x = 42 -> yields o.x
            a + "|" + b + "|" + obj.x;
            """);

        // first yield = 1; resumed with 42 so o.x becomes 42 and the second yield reads it; obj.x persists.
        Assert.Equal("1|42|42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: a computed-key write across a yield. The base AND the key both sit on the operand stack
    // across the suspension; on resume the write must target the same dynamically-keyed property.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedWriteAcrossYield_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                o[k] = yield 1;
                yield o[k];
            }

            var obj = {};
            var it = g(obj, "dynamicKey");
            var a = it.next().value;       // yields 1
            var b = it.next(7).value;      // resumes with 7 -> o["dynamicKey"] = 7
            a + "|" + b + "|" + obj.dynamicKey;
            """);

        Assert.Equal("1|7|7", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // End-to-end: a `this` property write inside a generator invoked with a receiver. The write must hit
    // the bound receiver, which survives the suspension on the resume state.
    [Fact(Timeout = 5000)]
    public async Task GeneratorThisWriteAcrossYield_RoutesResumableAndMutatesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                this.count = yield 1;
                yield this.count;
            }

            var receiver = {};
            var it = g.call(receiver);
            var a = it.next().value;       // yields 1
            var b = it.next(5).value;      // resumes with 5 -> this.count = 5
            a + "|" + b + "|" + receiver.count;
            """);

        Assert.Equal("1|5|5", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorNamedSuperWriteAcrossYield_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                set value(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                *g() {
                    super.value = yield 1;
                    yield this.seen;
                }
            }

            var target = new Derived();
            var it = target.g();
            var a = it.next().value;
            var b = it.next(42).value;
            a + "|" + b + "|" + target.seen;
            """);

        Assert.Equal("1|42|42", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedSuperWriteAcrossYield_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                set dynamic(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                *g(key) {
                    super[key] = yield 1;
                    yield this.seen;
                }
            }

            var target = new Derived();
            var it = target.g("dynamic");
            var a = it.next().value;
            var b = it.next(7).value;
            a + "|" + b + "|" + target.seen;
            """);

        Assert.Equal("1|7|7", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial: a strict-mode generator writing to a NON-writable property must throw a TypeError when
    // the SetNamedProperty step runs — proving the resumable handler honors strictness through
    // context.CurrentScope.IsStrict (SetPropertyValue) rather than silently swallowing the write.
    [Fact(Timeout = 5000)]
    public async Task GeneratorStrictWriteToReadOnlyProperty_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                "use strict";
                yield 1;
                o.frozen = 2;   // o.frozen is non-writable: strict assignment must throw
                yield 3;
            }

            var obj = {};
            Object.defineProperty(obj, "frozen", { value: 1, writable: false });
            var it = g(obj);
            var first = it.next().value;   // 1
            var caught = "none";
            try {
                it.next();                 // runs the strict write -> TypeError
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught + "|" + obj.frozen;
            """);

        // The strict write throws TypeError and leaves the non-writable property untouched.
        Assert.Equal("1|TypeError|1", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial: a sloppy-mode write to the same non-writable property must SILENTLY fail (no throw,
    // value unchanged), the complement of the strict case — proving the resumable handler routes the
    // sloppy semantics correctly too.
    [Fact(Timeout = 5000)]
    public async Task GeneratorSloppyWriteToReadOnlyProperty_SilentlyIgnored()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield 1;
                o.frozen = 2;   // sloppy mode: silently ignored
                yield 3;
            }

            var obj = {};
            Object.defineProperty(obj, "frozen", { value: 1, writable: false });
            var it = g(obj);
            var first = it.next().value;   // 1
            var second = it.next().value;  // 3 (the write was a no-op, no throw)
            first + "|" + second + "|" + obj.frozen;
            """);

        Assert.Equal("1|3|1", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorSuperWriteToThrowingSetter_PropagatesThrow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                set frozen(value) {
                    throw new TypeError("blocked");
                }
            }

            class Derived extends Base {
                *g() {
                    yield 1;
                    super.frozen = 2;
                    yield 3;
                }
            }

            var target = new Derived();
            var it = target.g();
            var first = it.next().value;
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught + "|" + Object.prototype.hasOwnProperty.call(target, "frozen");
            """);

        Assert.Equal("1|TypeError|false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_NamedSuperPropertyCompoundAssignmentAcrossYield_AdmitsReadAndWrite()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() {
                    return this.seen ?? 1;
                }

                set value(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                *g() {
                    super.value += yield 1;
                    yield this.seen;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedSuperProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.SetNamedSuperProperty);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorNamedSuperCompoundAssignmentAcrossYield_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() {
                    return this.seen ?? 1;
                }

                set value(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                *g() {
                    super.value += yield 1;
                    yield this.seen;
                }
            }

            var target = new Derived();
            var it = target.g();
            var a = it.next().value;
            var b = it.next(41).value;
            a + "|" + b + "|" + target.seen;
            """);

        Assert.Equal("1|42|42", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end async: an async function that writes a property after an await suspension routes through
    // the resumable fast path and the mutation persists on the resolved object.
    [Fact(Timeout = 5000)]
    public async Task AsyncPropertyWriteAcrossAwait_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(o) {
                await o.gate;
                o.written = 11;       // property write after the await suspension point
                return o.written;
            }

            var target = { gate: Promise.resolve(0) };
            run(target).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(11d, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // Regression: a strict-mode write to a non-writable property AFTER an await must REJECT the
    // promise with TypeError, not leave it permanently pending. The strict write surfaces as a
    // ThrowSignal CLR exception (not a resumable Throw step), so the bytecode async-resume drive
    // (DriveUnifiedBytecodeToCompletion) must catch it and reject — the synchronous generator path
    // works only incidentally because .next() runs inside JS try/catch on the call stack.
    [Fact(Timeout = 5000)]
    public async Task AsyncStrictWriteToReadOnlyAfterAwait_RejectsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "PENDING";
            async function run(o) {
                "use strict";
                await o.gate;
                o.ro = 2;             // strict write to a non-writable property after the await
                return "RESOLVED";
            }

            var target = { gate: Promise.resolve(0) };
            Object.defineProperty(target, "ro", { value: 9, writable: false });
            run(target).then(value => asyncResult = value, err => asyncResult = err.constructor.name);
            asyncResult;
            """);

        Assert.Equal("TypeError", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncSuperPropertyWriteAcrossAwait_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            class Base {
                set value(v) {
                    this.seen = v;
                }
            }

            class Derived extends Base {
                async run() {
                    await this.gate;
                    super.value = 11;
                    return this.seen;
                }
            }

            var target = new Derived();
            target.gate = Promise.resolve(0);
            target.run().then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(11d, result);
        AssertAsyncFastPath("run", argc: 0);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal) &&
                      (record.Message.Contains($"func={functionName}", StringComparison.Ordinal) ||
                       record.Message.Contains("func=<anonymous>", StringComparison.Ordinal)));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal) &&
                      (record.Message.Contains($"func={functionName}", StringComparison.Ordinal) ||
                       record.Message.Contains("func=<anonymous>", StringComparison.Ordinal)));

    private static ExecutionPlan GetClassMethodPlan(string source, string className, string methodName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<ClassDeclaration>(pipeline.Analyzed.Body
            .Single(node =>
                node is ClassDeclaration classDeclaration &&
                classDeclaration.Name.Name == className));
        var method = Assert.Single(declaration.Definition.Members.Where(member => member.Name == methodName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)method.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
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

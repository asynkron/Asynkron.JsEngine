using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for property UPDATES (<c>o.x++</c>, <c>o[k]--</c>, prefix and postfix) and property
///     DELETES (<c>delete o.x</c>, <c>delete o[k]</c>) inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async whose body
///     updates or deletes a property is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />; before this slice the
///     resumable opcode allowlist (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.UpdateNamedProperty" /> /
///     <see cref="UnifiedBytecodeOpCode.UpdateComputedProperty" /> /
///     <see cref="UnifiedBytecodeOpCode.DeleteNamedProperty" /> /
///     <see cref="UnifiedBytecodeOpCode.DeleteComputedProperty" />, so any property update/delete fell
///     back to the interpreter. This closes burn-down items B6, B7, B9, B10.
///
///     The update/delete opcodes operate purely on the operand stack — the base (and, for the computed
///     form, the key) sit on <see cref="UnifiedBytecodeResumeState.OperandStack" /> across any suspension
///     in a sibling sub-expression and are restored on resume, the same mechanism the already-admitted
///     property READS and WRITES rely on. The opcodes themselves cannot suspend (no AwaitedProgram), so
///     they always run to completion inside a single resumable step. The resumable handlers reuse the sync
///     VM's <c>UpdatePropertyValue</c> / <c>DeleteNamedProperty</c> / <c>DeleteComputedProperty</c>
///     helpers, threading the body's own strictness (<c>state.IsStrict</c>), so strict semantics (a strict
///     update of a read-only property, or a strict delete of a non-configurable property, throwing) are
///     preserved.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (a fall-back to the interpreter fails the test) — and (b)
///     correctness for the adversarial cases: postfix vs prefix value semantics, a computed-key update,
///     a strict read-only update still throwing, a delete that crosses a yield, a strict delete of a
///     non-configurable property throwing, and an async update across an await.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumablePropertyUpdateDeleteTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that UPDATES a named property is now admitted, and the admitted program
    // actually carries UpdateNamedProperty (proving the slice expanded eligibility to the property-update
    // tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_NamedPropertyUpdate_AdmitsUpdateNamedProperty()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                yield 1;
                o.x++;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedProperty);
    }

    // The gate: a computed-key update (`o[k]++`) is admitted and carries UpdateComputedProperty.
    [Fact]
    public void EvaluateResumable_ComputedPropertyUpdate_AdmitsUpdateComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function* g(o, k) {
                yield 1;
                o[k]++;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedProperty);
    }

    [Fact]
    public void EvaluateResumable_NamedSuperPropertyUpdate_AdmitsUpdateNamedSuperProperty()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() {
                    return this._value ?? 10;
                }

                set value(next) {
                    this._value = next;
                }
            }

            class Derived extends Base {
                *g() {
                    yield 1;
                    yield super.value++;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateNamedSuperProperty);
    }

    [Fact]
    public void EvaluateResumable_ComputedSuperPropertyUpdate_AdmitsUpdateComputedSuperProperty()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() {
                    return this._value ?? 10;
                }

                set value(next) {
                    this._value = next;
                }
            }

            class Derived extends Base {
                *g(key) {
                    yield 1;
                    yield --super[key];
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateComputedSuperProperty);
    }

    // The gate: a named delete (`delete o.x`) is admitted and carries DeleteNamedProperty.
    [Fact]
    public void EvaluateResumable_NamedPropertyDelete_AdmitsDeleteNamedProperty()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                yield 1;
                yield delete o.x;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteNamedProperty);
    }

    // The gate: a computed delete (`delete o[k]`) is admitted and carries DeleteComputedProperty.
    [Fact]
    public void EvaluateResumable_ComputedPropertyDelete_AdmitsDeleteComputedProperty()
    {
        var plan = GetFunctionPlan("""
            function* g(o, k) {
                yield 1;
                yield delete o[k];
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeleteComputedProperty);
    }

    // End-to-end: a POSTFIX named update inside a generator. The expression value is the OLD value; the
    // object property holds the NEW value. The base survives the suspension on the resume state and the
    // mutation persists on the real object.
    [Fact(Timeout = 5000)]
    public async Task GeneratorPostfixNamedUpdate_RoutesResumableAndUsesOldValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield 1;
                yield o.x++;     // postfix: yields the OLD value, o.x becomes new
                yield o.x;
            }

            var obj = { x: 10 };
            var it = g(obj);
            var a = it.next().value;   // 1
            var b = it.next().value;   // 10 (old value of o.x)
            var c = it.next().value;   // 11 (new value)
            a + "|" + b + "|" + c + "|" + obj.x;
            """);

        Assert.Equal("1|10|11|11", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: a PREFIX named update yields the NEW value, the complement of the postfix case.
    [Fact(Timeout = 5000)]
    public async Task GeneratorPrefixNamedUpdate_RoutesResumableAndUsesNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield 1;
                yield --o.x;     // prefix: yields the NEW value
                yield o.x;
            }

            var obj = { x: 10 };
            var it = g(obj);
            var a = it.next().value;   // 1
            var b = it.next().value;   // 9 (new value)
            var c = it.next().value;   // 9
            a + "|" + b + "|" + c + "|" + obj.x;
            """);

        Assert.Equal("1|9|9|9", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: a computed-key update (`o[k]++`) inside a generator. The dynamic key must address the
    // same property across the suspension; the mutation persists on the object.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedUpdate_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield 1;
                yield o[k]++;
                yield o[k];
            }

            var obj = { dynamicKey: 5 };
            var it = g(obj, "dynamicKey");
            var a = it.next().value;   // 1
            var b = it.next().value;   // 5 (old)
            var c = it.next().value;   // 6 (new)
            a + "|" + b + "|" + c + "|" + obj.dynamicKey;
            """);

        Assert.Equal("1|5|6|6", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPostfixNamedSuperUpdate_RoutesResumableAndUsesOldValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() {
                    return this._value ?? 10;
                }

                set value(next) {
                    this._value = next;
                }
            }

            class Derived extends Base {
                *g() {
                    yield 1;
                    var observed = super.value++;
                    yield observed;
                    yield this._value;
                }
            }

            var target = new Derived();
            var it = target.g();
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            a + "|" + b + "|" + c + "|" + target._value;
            """);

        Assert.Equal("1|10|11|11", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPrefixComputedSuperUpdate_RoutesResumableAndUsesNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() {
                    return this._value ?? 10;
                }

                set value(next) {
                    this._value = next;
                }
            }

            class Derived extends Base {
                *g(key) {
                    yield 1;
                    var observed = --super[key];
                    yield observed;
                    yield this._value;
                }
            }

            var target = new Derived();
            var it = target.g("value");
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            a + "|" + b + "|" + c + "|" + target._value;
            """);

        Assert.Equal("1|9|9|9", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: `delete o.x` inside a generator. The delete result is a boolean and the property is
    // actually removed from the object after the generator resumes.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNamedDelete_RoutesResumableAndRemovesProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield 1;
                yield delete o.x;     // true
                yield ("x" in o);     // false
            }

            var obj = { x: 1 };
            var it = g(obj);
            var a = it.next().value;   // 1
            var b = it.next().value;   // true
            var c = it.next().value;   // false
            a + "|" + b + "|" + c + "|" + ("x" in obj);
            """);

        Assert.Equal("1|true|false|false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: `delete o[k]` with a dynamic key inside a generator. The key is computed and survives
    // the suspension; the targeted property is removed.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedDelete_RoutesResumableAndRemovesProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield 1;
                yield delete o[k];
                yield (k in o);
            }

            var obj = { gone: 7 };
            var it = g(obj, "gone");
            var a = it.next().value;   // 1
            var b = it.next().value;   // true
            var c = it.next().value;   // false
            a + "|" + b + "|" + c + "|" + ("gone" in obj);
            """);

        Assert.Equal("1|true|false|false", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // Adversarial: a strict-mode generator UPDATING a NON-writable property must throw TypeError when the
    // UpdateNamedProperty step runs — proving the resumable handler honors strictness via state.IsStrict
    // (UpdatePropertyValue) rather than silently swallowing the write.
    [Fact(Timeout = 5000)]
    public async Task GeneratorStrictUpdateOfReadOnlyProperty_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                "use strict";
                yield 1;
                o.frozen++;          // non-writable: strict update must throw
                yield 3;
            }

            var obj = {};
            Object.defineProperty(obj, "frozen", { value: 1, writable: false });
            var it = g(obj);
            var first = it.next().value;   // 1
            var caught = "none";
            try {
                it.next();                 // runs the strict update -> TypeError
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught + "|" + obj.frozen;
            """);

        Assert.Equal("1|TypeError|1", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorSuperUpdateWithThrowingSetter_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get locked() {
                    return 1;
                }

                set locked(value) {
                    throw new TypeError("blocked");
                }
            }

            class Derived extends Base {
                *g() {
                    yield 1;
                    super.locked++;
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
            first + "|" + caught + "|" + target.locked;
            """);

        Assert.Equal("1|TypeError|1", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a strict-mode DELETE of a non-configurable property must throw TypeError, proving the
    // resumable delete handler threads strictness through DeleteNamedProperty.
    [Fact(Timeout = 5000)]
    public async Task GeneratorStrictDeleteOfNonConfigurable_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                "use strict";
                yield 1;
                delete o.locked;     // non-configurable: strict delete must throw
                yield 3;
            }

            var obj = {};
            Object.defineProperty(obj, "locked", { value: 1, configurable: false });
            var it = g(obj);
            var first = it.next().value;   // 1
            var caught = "none";
            try {
                it.next();                 // runs the strict delete -> TypeError
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught + "|" + ("locked" in obj);
            """);

        Assert.Equal("1|TypeError|true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial: a computed update on a null/undefined base must throw TypeError, mirroring the sync VM's
    // guard, instead of corrupting the operand stack.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedUpdateOnNullBase_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield 1;
                o[k]++;              // o is null -> TypeError
                yield 3;
            }

            var it = g(null, "x");
            var first = it.next().value;   // 1
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught;
            """);

        Assert.Equal("1|TypeError", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // End-to-end async: an async function that updates a property after an await suspension routes through
    // the resumable fast path and the mutation persists on the resolved object.
    [Fact(Timeout = 5000)]
    public async Task AsyncPropertyUpdateAcrossAwait_RoutesResumableAndPersists()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(o) {
                await o.gate;
                o.count++;            // property update after the await suspension point
                return o.count;
            }

            var target = { gate: Promise.resolve(0), count: 41 };
            run(target).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(42d, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // End-to-end async: a delete after an await. The property is removed and the resolved value reflects
    // the boolean delete result.
    [Fact(Timeout = 5000)]
    public async Task AsyncPropertyDeleteAcrossAwait_RoutesResumableAndRemovesProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(o) {
                await o.gate;
                var ok = delete o.victim;
                return ok + "|" + ("victim" in o);
            }

            var target = { gate: Promise.resolve(0), victim: 1 };
            run(target).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("true|false", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal) &&
                      (record.Message.Contains($"func={functionName}", StringComparison.Ordinal) ||
                       record.Message.Contains("func=<anonymous>", StringComparison.Ordinal)));

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
}

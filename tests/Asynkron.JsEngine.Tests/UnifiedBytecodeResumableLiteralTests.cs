using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for OBJECT literals (<c>{a, b: v, [computed]: v, ...spread}</c>) and ARRAY literals
///     (<c>[a, , b, ...spread]</c>) inside the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). A generator/async whose body
///     materializes an object or array literal is now admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" />; before this slice the
///     resumable opcode allowlist (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.CreateObject" /> /
///     <see cref="UnifiedBytecodeOpCode.CreateArray" /> and the family of Define* / ArrayPush* / *Spread
///     opcodes, so any object/array literal fell back to the interpreter.
///
///     A literal is built bottom-up on the operand stack: a single <c>Create{Object,Array}</c> pushes a
///     FRESH receiver, then the Define*/ArrayPush*/*Spread opcodes mutate it in place. A sub-expression can
///     suspend (<c>{a: yield 1}</c>, <c>yield [yield 1]</c>); the partially-built receiver plus any already
///     evaluated keys/values below the suspension point sit on
///     <see cref="UnifiedBytecodeResumeState.OperandStack" />, the resume state's stable backing store
///     restored on resume, exactly like the admitted property writes. The resumable handlers reuse the sync
///     VM's Define*/ApplyObjectLiteralSpread/EnumerateSpread helpers verbatim, so computed-key coercion,
///     name inference, own-enumerable spread copy (getter order), and array-hole semantics are identical.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (a fall-back to the interpreter fails the test) — and (b)
///     correctness for the adversarial cases: a literal whose element crosses a suspension, computed keys,
///     shorthand, object spread with getter-order observation, array holes, array spread, a FRESH
///     object/array per evaluation across a yield, and an async literal across an await.
///
///     Object METHODS (<c>{ m(){} }</c>) and GET/SET accessors (<c>{ get x(){} }</c>) are admitted for
///     non-capturing function literals now that <see cref="UnifiedBytecodeOpCode.LoadFunctionLiteral" /> and
///     <see cref="UnifiedBytecodeOpCode.EnsureHasName" /> have resumable handlers. Capturing nested function
///     literals remain owned by <see cref="UnifiedBytecodeResumableNestedFunctionTests" /> and still decline.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableLiteralTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_ClassLiteralExplicitConstructor_AdmitsLoadClassLiteral()
    {
        var result = AssertClassLiteralEligible("""
            function* g() {
                yield class Box {
                    constructor(value) {
                        this.value = value;
                    }
                };
            }
            """);

        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassLiteralDefaultConstructors_AdmitLoadClassLiteral()
    {
        _ = AssertClassLiteralEligible("""
            function* g() {
                yield class {};
            }
            """);

        _ = AssertClassLiteralEligible("""
            class Base {}
            function* g() {
                yield class Derived extends Base {};
            }
            """);
    }

    [Theory]
    [InlineData("""
        class Base {
            get value() { return 41; }
        }

        function* g() {
            yield class Derived extends Base {
                read() { return super.value + 1; }
            };
        }
        """)]
    [InlineData("""
        class Base {
            get value() { return 41; }
        }

        function* g() {
            yield class Derived extends Base {
                get answer() { return super.value + 1; }
            };
        }
        """)]
    public void EvaluateResumable_ClassLiteralPublicMemberSuper_AdmitsLoadClassLiteral(string source)
    {
        _ = AssertClassLiteralEligible(source);
    }

    [Fact]
    public void EvaluateResumable_ClassLiteralPublicInstanceFieldSuper_AdmitsLoadClassLiteral()
    {
        _ = AssertClassLiteralEligible("""
            class Base {
                get value() { return 41; }
            }

            function* g() {
                yield class Derived extends Base {
                    answer = super.value + 1;
                };
            }
            """);
    }

    [Theory]
    [MemberData(nameof(DeclinedB24ClassLiteralPrograms))]
    public void EvaluateResumable_ClassLiteralOutsideB24_DeclinesForLaterB24Leaves(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("outside B24", result.Reason, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassLiteralExplicitConstructor_RoutesResumableAndConstructs()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield class Box {
                    constructor(value) {
                        this.value = value;
                    }
                };
            }

            var it = g();
            var Box = it.next().value;
            var box = new Box(7);
            Box.name + "|" + box.value + "|" + (box instanceof Box) + "|" + (Box.prototype.constructor === Box);
            """);

        Assert.Equal("Box|7|true|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassLiteralDefaultBaseConstructor_RoutesResumableAndConstructs()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield class {};
            }

            var it = g();
            var C = it.next().value;
            var instance = new C();
            (typeof C) + "|" + (instance instanceof C) + "|" + (C.prototype.constructor === C);
            """);

        Assert.Equal("function|true|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassLiteralDefaultDerivedConstructor_RoutesResumableAndForwardsSuper()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            function* g() {
                yield class Derived extends Base {};
            }

            var it = g();
            var Derived = it.next().value;
            var instance = new Derived(11);
            instance.value + "|" + (instance instanceof Derived) + "|" + (instance instanceof Base);
            """);

        Assert.Equal("11|true|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassLiteralPublicSuperMembers_RoutesResumableAndRunsSuperInitializers()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() { return 20; }
            }

            function* g() {
                yield 0;
                yield class Derived extends Base {
                    answer = super.value + 2;
                    read() { return super.value + this.answer; }
                };
            }

            var it = g();
            it.next();
            var Derived = it.next().value;
            var instance = new Derived();
            instance.answer + "|" + instance.read() + "|" + (instance instanceof Base);
            """);

        Assert.Equal("22|42|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncClassLiteralAfterAwait_RoutesResumableAndConstructs()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(gate) {
                await gate;
                return class AsyncBox {
                    constructor() {
                        this.value = 13;
                    }
                };
            }

            run(Promise.resolve(0)).then(C => {
                var instance = new C();
                asyncResult = C.name + "|" + instance.value + "|" + (instance instanceof C);
            });
            asyncResult;
            """);

        Assert.Equal("AsyncBox|13|true", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // The gate: a generator that yields an OBJECT literal whose value crosses a yield is now admitted, and
    // the admitted program actually carries CreateObject + DefineObjectProperty (proving the slice expanded
    // eligibility to the object-literal tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_ObjectLiteralAcrossYield_AdmitsCreateObjectAndDefineProperty()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield { x: yield 1 };
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateObject);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectProperty);
    }

    // The gate: a computed-key object literal across a yield is admitted and carries
    // DefineComputedObjectProperty.
    [Fact]
    public void EvaluateResumable_ComputedKeyObjectLiteral_AdmitsDefineComputed()
    {
        var plan = GetFunctionPlan("""
            function* g(k) {
                yield 0;
                yield { [k]: 99 };
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectProperty);
    }

    // The gate: an object spread (`{...src}`) is admitted and carries ObjectSpread.
    [Fact]
    public void EvaluateResumable_ObjectSpread_AdmitsObjectSpread()
    {
        var plan = GetFunctionPlan("""
            function* g(src) {
                yield 0;
                yield { a: 1, ...src };
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectSpread);
    }

    // The gate: an ARRAY literal with an element, a hole, and a spread is admitted and carries
    // CreateArray + ArrayPush + ArrayPushHole + ArraySpread.
    [Fact]
    public void EvaluateResumable_ArrayLiteral_AdmitsArrayOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(src) {
                yield 0;
                yield [1, , 3, ...src];
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CreateArray);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPush);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayPushHole);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArraySpread);
    }

    // The method/accessor gate: non-capturing object METHODS and GET/SET accessors now route because
    // LoadFunctionLiteral / EnsureHasName are admitted by ExecuteResumable. This covers the static and
    // computed member-definition opcodes that were already allowlisted by the literal slice.
    [Fact]
    public void EvaluateResumable_ObjectMethodAndAccessor_AdmitsFunctionLiteralMembers()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield 0;
                yield {
                    m() { return 1; },
                    [key]() { return 2; },
                    get x() { return 3; },
                    set x(v) { this.saved = v; },
                    get [key + "Value"]() { return 4; }
                };
            }
            """,
            "g");
        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectMethod);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectMethod);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineObjectAccessor);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DefineComputedObjectAccessor);
    }

    // End-to-end: an object literal whose property value crosses a yield routes through the resumable fast
    // path. The partially-built object survives the suspension on the operand stack and the resumed value
    // lands in the right property, with the earlier-evaluated property preserved below it.
    [Fact(Timeout = 5000)]
    public async Task GeneratorObjectLiteralAcrossYield_RoutesResumableAndBuildsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                var o = { a: 1, b: yield 10, c: 3 };
                yield o.a + "," + o.b + "," + o.c;
            }

            var it = g();
            var first = it.next().value;     // yields 10 (the b initializer)
            var second = it.next(2).value;   // resumes with 2 -> b = 2
            first + "|" + second;
            """);

        Assert.Equal("10|1,2,3", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end: computed key + shorthand in one object literal built after a yield. Exercises
    // DefineComputedObjectProperty (with ToPropertyKey coercion) and shorthand DefineObjectProperty through
    // the resumable handler.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedAndShorthandObjectLiteral_RoutesResumableAndBuildsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                var shorthand = 5;
                yield { shorthand, [key]: 99, plain: 7 };
            }

            var it = g("dyn");
            var o = it.next().value;     // the yielded object literal
            o.shorthand + "|" + o[("dyn")] + "|" + o.plain;
            """);

        Assert.Equal("5|99|7", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorObjectMethodAndAccessorLiteral_RoutesResumableAndDefinesMembers()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield {
                    m() { return 1; },
                    [key]() { return 2; },
                    get x() { return 3; },
                    set x(v) { this.saved = v; },
                    get [key + "Value"]() { return 4; }
                };
            }

            var it = g("dyn");
            var o = it.next().value;
            o.x = 9;
            o.m() + "|" + o.dyn() + "|" + o.x + "|" + o.saved + "|" + o.dynValue;
            """);

        Assert.Equal("1|2|3|9|4", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: an object spread copying own-enumerable properties IN ORDER, with getters on the source
    // observed during the copy. Proves ApplyObjectLiteralSpread runs through the resumable handler with the
    // same own-enumerable + getter-invocation-order semantics as the sync path.
    [Fact(Timeout = 5000)]
    public async Task GeneratorObjectSpread_RoutesResumableAndCopiesOwnEnumerableInOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            // The getter source is built OUTSIDE the generator so this test isolates spread getter order
            // from the separate nested-function-literal route.
            var order = [];
            var src = {};
            Object.defineProperty(src, "first", { enumerable: true, get: function () { order.push("first"); return 1; } });
            Object.defineProperty(src, "second", { enumerable: true, get: function () { order.push("second"); return 2; } });
            Object.defineProperty(src, "hidden", { enumerable: false, value: 99 });

            function* g(source) {
                yield { a: 0, ...source, last: 3 };
            }

            var it = g(src);
            var o = it.next().value;     // the yielded spread object
            o.a + "," + o.first + "," + o.second + "," + (o.hidden === undefined) + "|" + order.join(">");
            """);

        // own-enumerable first/second copied (getters invoked in declaration order); non-enumerable hidden
        // skipped; a kept; last kept.
        Assert.Equal("0,1,2,true|first>second", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: an array literal with an element, an elision hole, and a spread, yielded after a
    // suspension. Proves CreateArray / ArrayPush / ArrayPushHole / ArraySpread route through the resumable
    // handler with correct hole-vs-defined and length semantics. (Cross-suspension survival of a
    // partially-built receiver is proven by the object-literal-across-yield test above; here the focus is
    // the array opcode family — holes and spread.)
    [Fact(Timeout = 5000)]
    public async Task GeneratorArrayLiteralWithHolesAndSpread_RoutesResumableAndPreservesSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(src) {
                yield [1, , 3, ...src];
            }

            var it = g([7, 8]);
            var a = it.next().value;     // the yielded array literal
            a.length + "|" + (1 in a) + "|" + a[0] + "|" + a[2] + "|" + a[3] + "," + a[4];
            """);

        // elements: 1, hole, 3, 7, 8 => length 5; index 1 is a hole (not "in"); a[0]=1; a[2]=3; spread
        // a[3]=7, a[4]=8.
        Assert.Equal("5|false|1|3|7,8", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial / fresh-per-eval: a generator that yields a NEW object literal on every loop turn must
    // produce a DISTINCT object each time (ECMAScript requires fresh allocation per evaluation). If the
    // resumable handler cached the receiver, the two yielded objects would be reference-equal. The literal
    // is yielded (not stored in a var-with-yield-initializer) so each turn re-runs CreateObject.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreshObjectPerEvaluation_AreDistinctInstances()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                for (var i = 0; i < 2; i++) {
                    yield { id: i };
                }
            }

            var it = g();
            var o0 = it.next().value;    // { id: 0 }
            var o1 = it.next().value;    // { id: 1 }
            (o0 !== o1) + "|" + o0.id + "," + o1.id;
            """);

        Assert.Equal("true|0,1", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a fresh ARRAY per evaluation across yields — distinct instances, never cached.
    [Fact(Timeout = 5000)]
    public async Task GeneratorFreshArrayPerEvaluation_AreDistinctInstances()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                for (var i = 0; i < 2; i++) {
                    yield [i];
                }
            }

            var it = g();
            var a0 = it.next().value;    // [0]
            var a1 = it.next().value;    // [1]
            (a0 !== a1) + "|" + a0[0] + "," + a1[0];
            """);

        Assert.Equal("true|0,1", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: a computed key whose ToPropertyKey (toString) throws inside the object literal must
    // surface as a thrown error from the generator, proving the resumable handler honors the computed-key
    // coercion throw rather than swallowing it (the throw becomes the resumable Throw step).
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedKeyCoercionThrows_PropagatesError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(bad) {
                yield 1;
                yield { [bad]: 5 };
                yield "unreachable";
            }

            var bad = { toString: function () { throw new TypeError("bad key"); } };
            var it = g(bad);
            it.next();                   // yields 1
            var caught = "none";
            try {
                it.next();               // builds the literal -> computed key coercion throws
            } catch (e) {
                caught = e.constructor.name + ":" + e.message;
            }
            caught;
            """);

        Assert.Equal("TypeError:bad key", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Adversarial: an array spread over a non-iterable must throw a TypeError from the resumable ArraySpread
    // handler (EnumerateSpread's iterator lookup fails), surfacing as the resumable Throw step.
    [Fact(Timeout = 5000)]
    public async Task GeneratorArraySpreadOverNonIterable_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(src) {
                yield 1;
                yield [...src];
                yield "unreachable";
            }

            var it = g(42);              // 42 is not iterable
            it.next();                   // yields 1
            var caught = "none";
            try {
                it.next();               // builds [...42] -> TypeError
            } catch (e) {
                caught = e.constructor.name;
            }
            caught;
            """);

        Assert.Equal("TypeError", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end async: an async function that builds an object literal AFTER an await suspension routes
    // through the resumable fast path; the literal (including a computed key derived from the awaited value)
    // is materialized correctly on resume.
    [Fact(Timeout = 5000)]
    public async Task AsyncObjectLiteralAcrossAwait_RoutesResumableAndBuildsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            // `seed` is a parameter (no `var x = await` form, which is a separate resumable boundary). The
            // await suspends the activation; on resume the object literal — including a computed key derived
            // from the parameter — is materialized through the resumable VM.
            async function run(seed, gate) {
                await gate;
                return { base: seed, derived: seed * 2, [("k" + seed)]: 9 };
            }

            run(5, Promise.resolve(0)).then(value => asyncResult = value.base + "|" + value.derived + "|" + value.k5);
            asyncResult;
            """);

        Assert.Equal("5|10|9", result);
        AssertAsyncFastPath("run", argc: 2);
    }

    // End-to-end async: an async function that builds an ARRAY literal with a spread after an await.
    [Fact(Timeout = 5000)]
    public async Task AsyncArraySpreadAcrossAwait_RoutesResumableAndBuildsArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(tail, gate) {
                await gate;
                return [0, ...tail, 9];
            }

            run([1, 2, 3], Promise.resolve(0)).then(value => asyncResult = value.length + "|" + value.join(","));
            asyncResult;
            """);

        Assert.Equal("5|0,1,2,3,9", result);
        AssertAsyncFastPath("run", argc: 2);
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

    public static TheoryData<string> DeclinedB24ClassLiteralPrograms { get; } = new()
    {
        """
        function* g() {
            yield class { field = 1; };
        }
        """,
        """
        function* g() {
            yield class { static field = 1; };
        }
        """,
        """
        function* g() {
            yield class { static {} };
        }
        """,
        """
        function* g() {
            yield class { get value() { return 1; } };
        }
        """,
        """
        function* g(name) {
            yield class { [name]() { return 1; } };
        }
        """,
        """
        function* g(Base) {
            yield class Derived extends Base {};
        }
        """
    };

    private static UnifiedBytecodeProductionEligibilityResult AssertClassLiteralEligible(string source)
    {
        var plan = GetFunctionPlan(source, "g");
        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        return result;
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

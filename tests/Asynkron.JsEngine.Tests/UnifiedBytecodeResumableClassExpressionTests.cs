using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the narrow B24 class-expression field slices in the resumable VM.
///     Public non-computed instance fields without activation-capturing initializers and public
///     non-computed static fields may route, including the mixed public static+instance field subset,
///     activation-safe computed public instance class elements, computed instance field initializers
///     that read or capture resumable activation slots through the materialized body environment, and
///     activation-safe public static member / static-super-field subsets, while nearby class-element
///     families remain pre-VM declines.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClassExpressionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicInstanceFields_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    first = 2;
                    empty;
                    second = this.first + 1;
                    constructor(value) {
                        this.ctor = value;
                    }
                };
                var c = new C(7);
                yield c.second;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_GeneratorPublicStaticFieldClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var C = class {
                    static value = seed + 1;
                };
                return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncPublicStaticFieldClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run(seed) {
                await 0;
                var C = class {
                    static value = seed + 2;
                };
                return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_GeneratorStaticBlockClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var C = class Box {
                    static {
                        Box.value = seed + 1;
                    }
                };
                return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncStaticBlockClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run(seed) {
                await 0;
                var C = class Box {
                    static {
                        Box.value = seed + 2;
                    }
                };
                return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_PublicStaticAndInstanceFieldClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield "ready";
                var C = class {
                    static seed = 41;
                    value = this.constructor.seed + 1;
                };
                return new C().value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicAccessors_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var c = new C();
                c.value = 41;
                yield c.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncClassExpressionPublicAccessors_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run() {
                await 0;
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var c = new C();
                c.value = 41;
                return c.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedPublicInstanceElements_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    ["field"] = 40;

                    [("read")]() {
                        return this.field + 1;
                    }

                    get [("value")]() {
                        return this.read() + 1;
                    }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncClassExpressionComputedPublicInstanceElements_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run() {
                await 0;
                var C = class {
                    ["field"] = 41;

                    get [("value")]() {
                        return this.field + 1;
                    }
                };
                return new C().value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedComputedAndNonComputedPublicInstanceElements_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    ["field"] = 40;
                    extra = this.field + 1;

                    get value() {
                        return this.extra + 1;
                    }

                    plain() {
                        return this.extra;
                    }

                    [("read")]() {
                        return this.value + 1;
                    }
                };
                var c = new C();
                return c.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedPublicStaticElements_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    static ["field"] = 40;

                    static get [("value")]() {
                        return this.field + 2;
                    }

                    static [("read")]() {
                        return this.value + 1;
                    }
                };
                return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncClassExpressionComputedPublicStaticElements_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run() {
                await 0;
                var C = class {
                    static ["field"] = 40;

                    static get [("value")]() {
                        return this.field + 2;
                    }
                };
                return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedPublicAccessorWithActivationKey_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield 1;
                var C = class {
                    get [key]() { return 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedNameActivationWrite_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield 1;
                var C = class {
                    [key = "value"]() { return 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicInstanceFields_RoutesResumableAndInitializesFields()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 0;
                var C = class {
                    first = 2;
                    empty;
                    second = this.first + 1;
                    constructor(value) {
                        this.ctor = value;
                        this.order = this.first + ":" + this.second;
                    }
                };
                var c = new C(7);
                yield c.first + "|" + c.empty + "|" + c.second + "|" + c.ctor + "|" + c.order;
            }

            var it = g();
            var beforeClass = it.next().value;
            var afterClass = it.next().value;
            beforeClass + "|" + afterClass;
            """);

        Assert.Equal("0|2|undefined|3|7|2:3", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPublicStaticFieldClassExpression_RoutesResumableAndReadsClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var C = class {
                    static value = seed + 1;
                };
                return C.value;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncPublicStaticFieldClassExpression_RoutesResumableAndReadsClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run(seed) {
                await 0;
                var C = class {
                    static value = seed + 2;
                };
                return C.value;
            }

            run(40).then(value => output = value);
            output;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorStaticBlockClassExpression_RoutesResumableAndSyncsActivationWrites()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                var observed = 0;
                yield "ready";
                var C = class Box {
                    static {
                        Box.value = seed + 1;
                        observed = Box.value + 1;
                    }
                };
                return C.value + "|" + observed;
            }

            var iterator = g(40);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncStaticBlockClassExpression_RoutesResumableAndSyncsActivationWrites()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run(seed) {
                var observed = 0;
                await 0;
                var C = class Box {
                    static {
                        Box.value = seed + 2;
                        observed = Box.value + 1;
                    }
                };
                return C.value + "|" + observed;
            }

            run(40).then(value => output = value);
            output;
            """);

        Assert.Equal("42|43", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPublicStaticAndInstanceFieldClassExpression_RoutesResumableAndInitializesBoth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    static seed = 41;
                    static label = "box";
                    value = this.constructor.seed + 1;
                    name = this.constructor.label;
                };
                var c = new C();
                return C.seed + "|" + C.label + "|" + c.value + "|" + c.name;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41|box|42|box:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicAccessors_RoutesResumableAndPreservesDescriptorAndReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var descriptor = Object.getOwnPropertyDescriptor(C.prototype, "value");
                var receiver = { _value: 0 };
                descriptor.set.call(receiver, 41);
                return descriptor.get.call(receiver) + "|" +
                    descriptor.enumerable + "|" +
                    descriptor.configurable + "|" +
                    (descriptor.get !== undefined) + "|" +
                    (descriptor.set !== undefined);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|false|true|true|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncClassExpressionPublicAccessors_RoutesResumableAndPreservesDescriptorAndReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run() {
                await 0;
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var descriptor = Object.getOwnPropertyDescriptor(C.prototype, "value");
                var receiver = { _value: 0 };
                descriptor.set.call(receiver, 41);
                return descriptor.get.call(receiver) + "|" +
                    descriptor.enumerable + "|" +
                    descriptor.configurable + "|" +
                    (descriptor.get !== undefined) + "|" +
                    (descriptor.set !== undefined);
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("42|false|true|true|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceClassElements_RouteResumableAndResolveNames()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    ["field"] = 40;

                    [("read")]() {
                        return this.field + 1;
                    }

                    get [("value")]() {
                        return this.read() + 1;
                    }
                };
                var c = new C();
                var descriptor = Object.getOwnPropertyDescriptor(C.prototype, "value");
                return c.read() + "|" +
                    c.value + "|" +
                    descriptor.enumerable + "|" +
                    descriptor.configurable + "|" +
                    (descriptor.get !== undefined);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41|42|false|true|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorMixedComputedAndNonComputedPublicInstanceClassElements_RouteResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    ["field"] = 40;
                    extra = this.field + 1;

                    get value() {
                        return this.extra + 1;
                    }

                    plain() {
                        return this.extra;
                    }

                    [("read")]() {
                        return this.value + 1;
                    }
                };
                var c = new C();
                return c.value;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicStaticClassElements_RouteResumableAndResolveNames()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    static ["field"] = 40;

                    static get [("value")]() {
                        return this.field + 2;
                    }

                    static [("read")]() {
                        return this.value + 1;
                    }
                };
                return C.value;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceActivationName_RouteResumableAndResolveName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                var C = class {
                    get [key]() {
                        return 42;
                    }
                };
                return C;
            }

            var iterator = g("value");
            var first = iterator.next();
            var C = iterator.next().value;
            first.value + ":" + first.done + "|" + new C().value;
            """);

        Assert.Equal("ready:false|42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicStaticActivationName_RouteResumableAndResolveName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(name) {
                yield "ready";
                var C = class {
                    static [name] = 42;
                };
                return C;
            }

            var iterator = g("value");
            var first = iterator.next();
            var C = iterator.next().value;
            first.value + ":" + first.done + "|" + C.value;
            """);

        Assert.Equal("ready:false|42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceActivationWrite_RouteResumableAndSyncsName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                var C = class {
                    [key = "value"]() {
                        return 42;
                    }
                };
                var c = new C();
                return key + "|" + c.value();
            }

            var iterator = g("initial");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|value|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicStaticActivationUpdate_RouteResumableAndSyncsName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(index) {
                yield "ready";
                var C = class {
                    static [index++] = 42;
                };
                return index + "|" + C[0];
            }

            var iterator = g(0);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|1|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceActivationCall_RouteResumableAndResolveName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(read) {
                yield "ready";
                var C = class {
                    [read()]() {
                        return 42;
                    }
                };
                var c = new C();
                return c.value();
            }

            var iterator = g(() => "value");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceActivationCallArgument_RouteResumableAndResolveName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(read, helper) {
                yield "ready";
                var C = class {
                    [helper(read)]() {
                        return 42;
                    }
                };
                var c = new C();
                return c.value();
            }

            var iterator = g(() => "value", reader => reader());
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceActivationIifeName_RouteResumableAndResolveName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                var C = class {
                    [(() => key)()]() {
                        return 42;
                    }
                };
                var c = new C();
                return c.value();
            }

            var iterator = g("value");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicInstanceActivationDelete_RejectsStrictIdentifierDelete()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                var C = class {
                    [delete key]() {
                        return key;
                    }
                };
                var c = new C();
                return c.false();
            }

            var iterator = g("value");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """));

        Assert.Contains("SyntaxError", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Delete of an unqualified identifier", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicAccessorsWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() { return 41; }
                set value(next) { this.base = next + 1; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    get value() { return super.value + 1; }
                    set value(next) { super.value = next; }
                };
                var c = new C();
                c.value = 4;
                return c.value + "|" + c.base + "|" + (c instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|5|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncClassExpressionPublicAccessorsWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            class Base {
                get value() { return 41; }
                set value(next) { this.base = next + 1; }
            }

            async function run() {
                await 0;
                var C = class extends Base {
                    get value() { return super.value + 1; }
                    set value(next) { super.value = next; }
                };
                var c = new C();
                c.value = 4;
                return c.value + "|" + c.base + "|" + (c instanceof Base);
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("42|5|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicMethodWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                value() { return this.seed + 1; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    constructor(seed) { super(); this.seed = seed; }
                    value() { return super.value() + 1; }
                };
                var c = new C(40);
                return c.value() + "|" + (c instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicFieldInitializerWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(seed) { this.seed = seed; }
                get value() { return this.seed + 1; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    constructor(seed) { super(seed); }
                    field = super.value + 1;
                };
                var c = new C(40);
                return c.field + "|" + (c instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionStaticPublicAccessor_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    static get value() { return this.seed + 1; }
                    static set value(next) { this.seed = next + 1; }
                };
                C.value = 40;
                return C.value + "|" + C.seed;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|41:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionStaticPublicMethodWithSuper_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static value() { return 41; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    static value() { return super.value() + 1; }
                };
                var c = new C();
                return C.value() + "|" + (c instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionStaticPublicFieldWithSuper_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static get value() { return 41; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    static field = super.value + 1;
                };
                var c = new C();
                return C.field + "|" + (c instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionMixedPublicStaticFieldAndMethod_RoutesResumableAndInitializesInOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    static seed = 40;
                    static value() { return this.seed + 1; }
                    static field = this.value() + 1;
                };
                return C.field;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionMixedPublicStaticFieldAndMethodWithSuper_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static value() { return 40; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                static value() { return super.value() + 1; }
                static field = this.value() + 1;
            };
                return C.field;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Theory]
    [InlineData("""
        function* g(seed) {
            yield class {
                field = seed;
            };
        }
        """)]
    [InlineData("""
        function* g() {
            yield class {
                #value = 1;
                field = 2;
            };
        }
        """)]
    public void EvaluateResumable_ClassExpressionNonB24bShapes_Decline(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible, source);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedFieldWithActivationInitializer_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield 1;
                var current = seed;
                var C = class {
                    ["field"] = current;
                };
                current = seed + 1;
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedFieldWithActivationInitializer_RoutesResumableAndEscapedClassReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    ["field"] = current;
                };
                current = seed + 1;
                yield C;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            var C = second.value;
            var c = new C();
            first.value + ":" + first.done + "|" + c.field + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedFieldWithActivationClosureInitializer_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    ["field"] = function read() { return current; };
                };
                current = seed + 1;
                yield C;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            var C = second.value;
            var c = new C();
            first.value + ":" + first.done + "|" + c.field() + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("read");
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedNameDirectActivationCall_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(read) {
                yield 1;
                var C = class {
                    [read()]() { return 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedNameActivationCallArgument_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(read, helper) {
                yield 1;
                var C = class {
                    [helper(read)]() { return 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedNameActivationDelete_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield 1;
                var C = class {
                    [delete key]() { return 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedNameNestedActivationCaptureIife_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield 1;
                var C = class {
                    [(() => key)()]() { return 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedNameNestedActivationCaptureEscapes_AdmitLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield 1;
                var leaked;
                var current = key;
                var C = class {
                    [(() => { leaked = function read() { return current; }; return "value"; })()]() { return 1; }
                };
                current = key + "!";
                return leaked;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedNameNestedActivationCaptureEscapes_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                var leaked;
                var current = key;
                var C = class {
                    [(() => { leaked = function read() { return current; }; return "value"; })()]() { return 1; }
                };
                current = key + "!";
                yield leaked;
            }

            var iterator = g("seed");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value() + ":" + second.done;
            """);

        Assert.Equal("ready:false|seed!:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("read");
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedStaticFieldWithActivationInitializer_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g(seed) {
            yield 1;
            var current = seed;
            var C = class {
                static ["field"] = current;
                static ["updated"] = (current = current + 1);
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.False(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedStaticFieldWithActivationInitializer_RoutesResumableAndSyncsStaticValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static ["field"] = current;
                    static ["updated"] = (current = current + 1);
                };
                yield C.field + ":" + C.updated + ":" + current;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41:42:42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedStaticFieldClosureInitializer_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g(seed) {
            yield 1;
            var C = class {
                static ["read"] = () => seed;
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedStaticFieldClosureInitializer_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static ["read"] = () => current;
                };
                current = seed + 1;
                yield C.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberBodyCapturesActivation_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g(seed) {
            yield 1;
            var C = class {
                ["read"]() { return seed; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberBodyCapturesActivation_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    ["read"]() { return current; }
                };
                current = seed + 1;
                var c = new C();
                yield c.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberConstructorCapturesActivation_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g(seed) {
            yield 1;
            var C = class Box {
                ["read"]() { return this.value; }
                constructor() { this.value = seed; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberConstructorCapturesActivation_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class Box {
                    ["read"]() { return this.value; }
                    constructor() { this.value = current; }
                };
                current = seed + 1;
                var c = new C();
                yield c.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("Box");
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateNeighbor_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                ["read"]() { return 42; }
                #secret() { return 41; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateNeighbor_RoutesResumableAndInvokesComputedMethod()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    ["read"]() { return 42; }
                    #secret() { return 41; }
                };
                var c = new C();
                yield c.read();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateFieldNeighbor_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                ["read"]() { return this.#value + 1; }
                #value = 41;
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateFieldNeighbor_RoutesResumableAndReadsPrivateField()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    ["read"]() { return this.#value + 1; }
                    #value = 41;
                };
                var c = new C();
                yield c.read();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateAccessorNeighbor_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                ["read"]() { return this.#value + 1; }
                get #value() { return 41; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateAccessorNeighbor_RoutesResumableAndReadsPrivateAccessor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    ["read"]() { return this.#value + 1; }
                    get #value() { return 41; }
                };
                var c = new C();
                yield c.read();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateAccessorCapturingActivation_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g(seed) {
            var current = seed;
            yield 1;
            var C = class {
                ["read"]() { return this.#value + 1; }
                get #value() { return current; }
            };
            current = seed + 1;
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateAccessorCapturingActivation_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                var current = seed;
                yield "ready";
                var C = class {
                    ["read"]() { return this.#value + 1; }
                    get #value() { return current; }
                };
                current = seed + 1;
                var c = new C();
                yield c.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|43:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateFieldInitializerCapturingActivation_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g(seed) {
            var current = seed;
            yield 1;
            var C = class {
                ["read"]() { return this.#value + 1; }
                #value = current;
            };
            current = seed + 1;
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateFieldInitializerCapturingActivation_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                var current = seed;
                yield "ready";
                var C = class {
                    ["read"]() { return this.#value + 1; }
                    #value = current;
                };
                current = seed + 1;
                var c = new C();
                yield c.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|43:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateFieldInitializerSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        class Base {
            get value() { return 41; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                ["read"]() { return this.#value; }
                #value = super.value;
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateFieldInitializerSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() { return 41; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    ["read"]() { return this.#value + 1; }
                    #value = super.value;
                };
                var c = new C();
                yield c.read();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateStaticFieldNeighbor_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                ["read"]() { return 42; }
                static #value = 41;
                static value() { return this.#value + 1; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateStaticFieldNeighbor_RoutesResumableAndReadsPrivateStaticField()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    ["read"]() { return 42; }
                    static #value = 41;
                    static value() { return this.#value + 1; }
                };
                var c = new C();
                yield C.value() + c.read();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|84:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateStaticFieldActivationInitializer_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            var current = 1;
            yield 1;
            var C = class {
                ["read"]() { return 42; }
                static #value = (current = current + 1);
                static value() { return this.#value; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.False(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedMemberWithPrivateStaticFieldActivationInitializer_RoutesResumableAndSyncsPrivateStaticValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                var current = seed;
                yield "ready";
                var C = class {
                    ["read"]() { return 42; }
                    static #value = (current = current + 1);
                    static value() { return this.#value; }
                };
                var c = new C();
                yield C.value() + ":" + current + ":" + c.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:42:42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedMemberWithPrivateStaticFieldInitializerSuper_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
            static get value() { return 41; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                ["read"]() { return 42; }
                static #value = super.value;
                static value() { return this.#value; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicAccessor_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                static get value() { return 1; }
            };
            return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPublicStaticFieldAndAccessor_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                static seed = 40;
                static get value() { return this.seed + 1; }
            };
            return C.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPublicStaticFieldAndMethod_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        function* g() {
            yield 1;
            var C = class {
                static seed = 40;
                static value() { return this.seed + 1; }
                static field = this.value() + 1;
            };
            return C.field + C.value();
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
        Assert.False(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionCapturingPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g(seed) {
            yield 1;
            var C = class {
                get value() { return seed; }
            };
            return new C().value;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicAccessorCapturesActivation_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g(seed) {
            yield 1;
            var C = class {
                static get value() { return seed; }
            };
            return C.value;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPublicStaticMemberCapturesActivation_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g(seed) {
            yield 1;
            var C = class {
                static field = 1;
                static get value() { return seed; }
            };
            return C.value;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicMemberConstructorCapturesActivation_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g(seed) {
            yield 1;
            var C = class {
                constructor() { this.value = seed; }
                static get value() { return 1; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicAccessorWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            class Base {
                get value() { return 41; }
                set value(next) { this.base = next + 1; }
            }

            function* g() {
                yield 1;
                var C = class extends Base {
                    get value() { return super.value + 1; }
                    set value(next) { super.value = next; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicMethodWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            class Base {
                value() { return 41; }
            }

            function* g() {
                yield 1;
                var C = class extends Base {
                    value() { return super.value() + 1; }
                };
                return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicFieldInitializerWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            class Base {
                get value() { return 41; }
            }

            function* g() {
                yield 1;
                var C = class extends Base {
                    field = super.value + 1;
                };
                return new C().field;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionCapturingPublicMethodWithSuper_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
            get value() { return 40; }
        }

        function* g(seed) {
            yield 1;
            var C = class extends Base {
                value() { return super.value + seed; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionCapturingPublicFieldInitializerWithSuper_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
            get value() { return 40; }
        }

        function* g(seed) {
            yield 1;
            var C = class extends Base {
                field = super.value + seed;
            };
            return new C().field;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicMethodWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        class Base {
            static value() { return 40; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                static value() { return super.value() + 1; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicMethodWithSuperCapturesActivation_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
            static value() { return 40; }
        }

        function* g(seed) {
            yield 1;
            var C = class extends Base {
                static value() { return super.value() + seed; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicFieldInitializerWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        class Base {
            static get value() { return 40; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                static field = super.value + 1;
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicFieldConstructorCapturesActivation_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
        }

        function* g(seed) {
            yield 1;
            var C = class extends Base {
                constructor() { super(); this.value = seed; }
                static field = 1;
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedPublicMethodWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        class Base {
            value() { return 40; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                ["value"]() { return super.value() + 1; }
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedPublicMethodWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                value() { return 40; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    ["value"]() { return super.value() + 1; }
                };
                var c = new C();
                yield c.value();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41:false", result);
        AssertGeneratorFastPath("g", argc: 0);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedPublicFieldInitializerWithSuper_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
        class Base {
            get value() { return 40; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                ["field"] = super.value + 1;
            };
            return C;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionComputedPublicFieldInitializerWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() { return 40; }
            }

            function* g() {
                yield "ready";
                var C = class extends Base {
                    ["field"] = super.value + 1;
                };
                var c = new C();
                yield c.field;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionPrivateMethodWithSuper_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
            value() { return 40; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                #value() { return super.value() + 1; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionPrivateFieldInitializerWithSuper_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        class Base {
            get value() { return 40; }
        }

        function* g() {
            yield 1;
            var C = class extends Base {
                #field = super.value + 1;
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPublicFieldAndPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g() {
            yield 1;
            var C = class {
                field = 1;
                get value() { return 2; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPrivateFieldAndPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g() {
            yield 1;
            var C = class {
                #value = 1;
                get value() { return this.#value; }
            };
            return C;
        }
        """);

    private static void AssertClassExpressionDeclines(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible, source);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("B24", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""
        function* g() {
            yield 1;
            var C = class {
                static #value = 1;
            };
            return C;
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = () => seed;
            };
            return C.value();
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = { read() { return seed; } };
            };
            return C.value.read();
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = { get read() { return seed; } };
            };
            return C.value.read;
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = class {
                    static read() { return seed; }
                };
            };
            return C.value.read();
        }
        """)]
    public void EvaluateResumable_UnownedClassExpressionShapes_DeclineBeforeVm(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("B24", result.Reason, StringComparison.Ordinal);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertProductionFastPath(string functionName) =>
        Assert.True(
            CurrentLogger!.Collector.Snapshot().Any(
                record => record.Message.Contains(
                    $"{ProductionFastPathLog} func={functionName}",
                    StringComparison.Ordinal)),
            string.Join(
                Environment.NewLine,
                CurrentLogger!.Collector.Snapshot()
                    .Select(static record => record.Message)
                    .Where(static message => message.Contains(
                        ProductionFastPathLog,
                        StringComparison.Ordinal))));

    [Fact(Timeout = 5000)]
    public async Task GeneratorStaticFieldClosureInitializer_FallsBackAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static read = () => current;
                };
                current = seed + 1;
                return C.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorStaticFieldObjectMethodInitializer_FallsBackAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static bag = { read() { return current; } };
                };
                current = seed + 1;
                return C.bag.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorStaticBlockClosure_FallsBackAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static {
                        this.read = () => current;
                    }
                };
                current = seed + 1;
                return C.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
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

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the narrow B36 direct root class-declaration slice in resumable bodies.
///     Simple class declarations can route through <see cref="UnifiedBytecodeOpCode.DeclareClass" />;
///     activation-safe computed public class declarations can route through the same instruction,
///     while unsafe neighboring class-definition state stays declined before VM execution.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClassDeclarationTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    [Fact]
    public void EvaluateResumable_ClassDeclaration_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield "ready";
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(7);
                yield box.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationComputedPublicElements_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield "ready";
                class Box {
                    [key = "value"]() {
                        return 42;
                    }

                    static ["seed"] = 7;
                }
                var box = new Box();
                yield box.value() + Box.seed;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationComputedNameActivationCall_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(read) {
                yield "ready";
                class Box {
                    [read()]() {
                        return 1;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationComputedNameActivationCall_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(read) {
                yield "ready";
                var key = "";
                class Box {
                    [key = read()]() {
                        return 42;
                    }
                }
                yield key + "|" + typeof Box;
            }

            var iterator = g(() => "value");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|value|function:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationComputedNameActivationDelete_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield "ready";
                class Box {
                    [delete key]() {
                        return 42;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtends_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {}
            function* g() {
                yield "ready";
                class Box extends Base {
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationActivationExtends_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(Base) {
                yield "ready";
                class Box extends Base {
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsComputedPublicMember_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(Base, key) {
                yield "ready";
                class Box extends Base {
                    [key = "value"]() {
                        return 42;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsComputedNameNestedActivationCaptureIife_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(Base, key) {
                yield "ready";
                class Box extends Base {
                    [(() => key)()]() {
                        return 1;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsComputedNameNestedActivationCaptureEscapes_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(Base, key) {
                yield "ready";
                var leaked;
                var current = key;
                class Box extends Base {
                    [(() => { leaked = function read() { return current; }; return "value"; })()]() {
                        return 1;
                    }
                }
                current = key + "!";
                yield leaked;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsComputedNameNestedActivationCaptureEscapes_RoutesResumableAndReadsLaterMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(key) {
                yield "ready";
                var leaked;
                var current = key;
                class Box extends Base {
                    [(() => { leaked = function read() { return current; }; return "value"; })()]() {
                        return 1;
                    }
                }
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
    public void EvaluateResumable_ClassDeclarationExtendsPublicMethodWithSuper_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                value() { return 41; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    value() { return super.value() + 1; }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPublicMethodWithSuperCapturesActivation_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                value() { return 40; }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    value() { return super.value() + seed; }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPublicInstanceMethod_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    value() { return seed + 1; }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPublicInstanceAccessor_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    get value() { return seed + 1; }
                    set value(next) { this.storage = next + seed; }
                }
                var box = new Box();
                box.value = 1;
                yield box.value + box.storage;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPrivateInstanceMethod_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            class Base {
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    #value() { return 1; }
                    read() { return 1; }
                }
                yield typeof Box;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.NotEqual(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPublicFieldWithSuper_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                get value() { return 41; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    field = super.value + 1;
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPublicFieldWithSuperCapturesActivation_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                get value() { return 40; }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    field = super.value + seed;
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsStaticPublicMethodWithSuper_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                static value() { return 41; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    static value() { return super.value() + 1; }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsStaticPublicMethodWithSuperCapturesActivation_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                static value() { return 40; }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static value() { return super.value() + seed; }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsStaticPublicFieldWithSuper_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {
                static get value() { return 41; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    static field = super.value + 1;
                }
                yield Box.field;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsPublicStaticField_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {}
            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static value = seed + 1;
                }
                yield Box.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationPublicStaticField_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static value = seed + 1;
                }
                yield Box.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationStaticFieldClosureInitializer_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static read = () => current;
                }
                current = seed + 1;
                yield Box.read();
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableClassDeclarationEnvironment(plan));
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedPublicStaticFieldAndMethod_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield "ready";
                class Box {
                    static seed = 40;
                    static value() { return this.seed + 1; }
                    static field = this.value() + 1;
                }
                yield Box.field + Box.value();
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
        Assert.False(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedPublicStaticFieldAndAccessor_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield "ready";
                class Box {
                    static seed = 40;
                    static get value() { return this.seed + 1; }
                    static set value(next) { this.seed = next + 1; }
                    static field = this.value + 1;
                }
                yield Box.field + Box.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
        Assert.False(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedPublicStaticMemberCapturesActivation_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static field = 1;
                    static get value() { return seed; }
                }
                yield Box.value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.NotEqual(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            "static member body captures activation binding 'seed'",
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationStaticFieldClosureAndStaticMemberCapturesActivation_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static read = () => current;
                    static value() { return seed; }
                }
                yield Box.value();
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.NotEqual(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            "static member body captures activation binding 'seed'",
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsStaticFieldClosureInitializer_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {}
            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static read = () => seed;
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExplicitDerivedConstructor_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(Base) {
                yield "ready";
                class Box extends Base {
                    constructor(value) {
                        super(value);
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExplicitDerivedConstructorCapturesActivation_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(Base, outer) {
                yield "ready";
                class Box extends Base {
                    constructor(value) {
                        super(outer);
                        this.value = value;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtendsStaticBlock_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {}
            function* g(seed) {
                var observed = 0;
                yield "ready";
                class Box extends Base {
                    static {
                        this.seed = seed + 1;
                        observed = this.seed + 1;
                    }
                }
                yield Box.seed + "|" + observed;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationStaticBlockClosure_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {}
            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static {
                        this.read = () => seed;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationStaticBlockDirectEvalLiteral_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                var current = seed;
                yield "ready";
                class Box {
                    static {
                        eval("current = current + 1");
                        this.value = current + 1;
                        current = this.value + 1;
                    }
                }
                yield Box.value + current;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedStaticFieldAndBlock_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static value = seed;
                    static {
                        Box.other = Box.value + 1;
                    }
                }
                yield Box.value + "|" + Box.other;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedStaticFieldAndBlockFunctionDeclaration_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static value = current;
                    static {
                        function readLater() { return current + Box.value; }
                        Box.readLater = readLater;
                    }
                }
                current = seed + 1;
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedStaticFieldAndBlockClassDeclaration_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static value = current;
                    static {
                        class Nested {
                            static value() { return current + Box.value; }
                        }
                        Box.Nested = Nested;
                    }
                }
                current = seed + 1;
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
        Assert.True(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationMixedStaticFieldAndBlockNestedClassDeclaration_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static value = seed;
                    static {
                        class Nested {
                            static value() { return 7; }
                        }
                        Box.Nested = Nested;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
        Assert.False(UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclaration_RoutesResumableAndKeepsBodyScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(7);
                yield box.value;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            var outside = typeof Box;
            first.value + ":" + first.done + "|" +
                second.value + ":" + second.done + "|" +
                outside;
            """);

        Assert.Equal("ready:false|7:false|undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicClassDeclaration_RoutesResumableAndSyncsName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                class Box {
                    [key = "value"]() {
                        return 42;
                    }

                    static ["seed"] = 7;
                }
                var box = new Box();
                yield key + "|" + box.value() + "|" + Box.seed;
            }

            var iterator = g("initial");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|value|42|7:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicClassDeclarationActivationDelete_RejectsStrictIdentifierDelete()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                class Box {
                    [delete key]() {
                        return key;
                    }
                }
                var box = new Box();
                yield box.false();
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
    public async Task GeneratorClassDeclarationExtends_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(seed) {
                    this.seed = seed;
                }

                read() {
                    return this.seed + 1;
                }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                }
                var box = new Box(41);
                yield box.read() + "|" + (box instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationActivationExtends_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeBase(extra) {
                return class Base {
                    constructor(seed) {
                        this.seed = seed;
                    }

                    read() {
                        return this.seed + extra;
                    }
                };
            }

            function* g(Base) {
                yield "ready";
                class Box extends Base {
                }
                var box = new Box(40);
                yield box.read() + "|" + (box instanceof Base);
            }

            var Base = makeBase(2);
            var iterator = g(Base);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsComputedPublicMember_RoutesResumableAndSyncsName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(key) {
                yield "ready";
                class Box extends Base {
                    [key = "value"]() {
                        return 42;
                    }
                }
                var box = new Box();
                yield key + "|" + box.value() + "|" + (box instanceof Base);
            }

            var iterator = g("initial");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|value|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsComputedIifeName_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(key) {
                yield "ready";
                class Box extends Base {
                    [(() => key)()]() {
                        return 42;
                    }
                }
                var box = new Box();
                yield box.value() + "|" + (box instanceof Base);
            }

            var iterator = g("value");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicMethodWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                value() { return this.seed + 1; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    constructor(seed) {
                        super();
                        this.seed = seed;
                    }

                    value() { return super.value() + 1; }
                }
                var box = new Box(40);
                yield box.value() + "|" + (box instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicMethodWithSuperCapturesActivation_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                value() { return this.seed + 1; }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    constructor(value) {
                        super();
                        this.seed = value;
                    }

                    value() { return super.value() + seed; }
                }
                var box = new Box(40);
                yield box.value() + "|" + (box instanceof Base);
            }

            var iterator = g(1);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicInstanceMethodCapturesActivation_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    value() { return seed + 1; }
                }
                var box = new Box();
                yield box.value() + "|" + (box instanceof Base);
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicInstanceAccessor_RoutesResumableAndPreservesDescriptor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    get value() { return seed + 1; }
                    set value(next) { this.storage = next + seed; }
                }
                var box = new Box();
                box.value = 1;
                yield box.value + "|" + box.storage + "|" + (box instanceof Base);
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicFieldWithSuper_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(seed) { this.seed = seed; }
                get value() { return this.seed + 1; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    constructor(seed) { super(seed); }
                    field = super.value + 1;
                }
                var box = new Box(40);
                yield box.field + "|" + (box instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicFieldWithSuperCapturesActivation_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(value) { this.seed = value; }
                get value() { return this.seed + 1; }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    constructor(value) { super(value); }
                    field = super.value + seed;
                }
                var box = new Box(40);
                yield box.field + "|" + (box instanceof Base);
            }

            var iterator = g(1);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsStaticPublicMethodWithSuper_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static value() { return 41; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    static value() { return super.value() + 1; }
                }
                var box = new Box();
                yield Box.value() + "|" + (box instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsStaticPublicMethodWithSuperCapturesActivation_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static value() { return 40; }
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static value() { return super.value() + seed; }
                }
                var box = new Box();
                yield Box.value() + "|" + (box instanceof Base);
            }

            var iterator = g(2);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsStaticPublicFieldWithSuper_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static get value() { return 41; }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                    static field = super.value + 1;
                }
                var box = new Box();
                yield Box.field + "|" + (box instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsPublicStaticField_RoutesResumableAndReadsActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static value = seed + 1;
                }
                var box = new Box();
                yield Box.value + "|" + (box instanceof Base);
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsStaticFieldClosureInitializer_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static read = () => seed + 1;
                }
                var box = new Box();
                yield Box.read() + "|" + (box instanceof Base);
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationPublicStaticField_RoutesResumableAndReadsActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static value = seed + 1;
                }
                yield Box.value;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationStaticFieldClosureInitializer_RoutesResumableAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static read = () => current;
                }
                current = seed + 1;
                yield Box.read();
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

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationMixedPublicStaticFieldAndMethod_RoutesResumableAndInitializesInOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                class Box {
                    static seed = 40;
                    static value() { return this.seed + 1; }
                    static field = this.value() + 1;
                }
                yield Box.field + "|" + Box.value();
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|41:false", result);
        AssertGeneratorFastPath("g", argc: 0);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationMixedPublicStaticFieldAndAccessor_RoutesResumableAndPreservesReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                class Box {
                    static seed = 40;
                    static get value() { return this.seed + 1; }
                    static set value(next) { this.seed = next + 1; }
                    static field = this.value + 1;
                }
                Box.value = 40;
                yield Box.field + "|" + Box.value + "|" + Box.seed;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|42|41:false", result);
        AssertGeneratorFastPath("g", argc: 0);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsStaticFieldSuperClosureInitializer_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static value = 41;
            }

            function* g(seed) {
                yield "ready";
                class Box extends Base {
                    static read = () => super.value + seed;
                }
                yield Box.read();
            }

            var iterator = g(1);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExplicitDerivedConstructor_RoutesResumableAndConstructorFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeBase(extra) {
                return class Base {
                    constructor(seed) {
                        this.seed = seed;
                    }

                    read() {
                        return this.seed + extra;
                    }
                };
            }

            function* g(Base) {
                yield "ready";
                class Box extends Base {
                    constructor(seed) {
                        super(seed);
                        this.local = seed + 1;
                    }
                }
                var box = new Box(40);
                yield box.read() + "|" + box.local + "|" + (box instanceof Base);
            }

            var Base = makeBase(2);
            var iterator = g(Base);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|41|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        var routedConstructor = logs.Any(
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=Box argc=1",
                StringComparison.Ordinal));
        Assert.True(
            routedConstructor,
            string.Join(
                Environment.NewLine,
                logs
                    .Select(static record => record.Message)
                    .Where(static message =>
                        message.Contains("Box", StringComparison.Ordinal) ||
                        message.Contains("SyncFunctionInvoker", StringComparison.Ordinal) ||
                        message.Contains("simple-ir", StringComparison.Ordinal) ||
                        message.Contains("unified-bytecode-production", StringComparison.Ordinal))));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExplicitDerivedConstructorCapturesActivation_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(seed) {
                    this.seed = seed;
                }

                read() {
                    return this.seed;
                }
            }

            function* g(Base, outer) {
                yield "ready";
                class Box extends Base {
                    constructor(value) {
                        super(outer);
                        this.value = value;
                    }
                }
                var box = new Box(7);
                yield box.read() + "|" + box.value + "|" + (box instanceof Base);
            }

            var iterator = g(Base, 35);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|35|7|true:false", result);
        AssertGeneratorFastPath("g", argc: 2);
        AssertProductionFastPath("Box");
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtendsStaticBlock_RoutesResumableAndStaticBlockFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(seed) {
                var observed = 0;
                yield "ready";
                class Box extends Base {
                    static {
                        this.seed = seed + 1;
                        observed = this.seed + 1;
                    }
                }
                var box = new Box();
                yield Box.seed + "|" + observed + "|" + (box instanceof Base);
            }

            var iterator = g(40);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationStaticBlockClosure_RoutesResumableAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
            }

            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box extends Base {
                    static {
                        this.read = () => current;
                    }
                }
                current = seed + 1;
                yield Box.read;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value() + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        AssertProductionFastPath("<anonymous>");
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationStaticBlockDirectEvalLiteral_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                var current = seed;
                yield "ready";
                class Box {
                    static {
                        eval("current = current + 1");
                        this.value = current + 1;
                        current = this.value + 1;
                    }
                }
                yield Box.value + "|" + current;
            }

            var iterator = g(40);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|43:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationMixedStaticFieldAndBlock_RoutesResumableAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static value = seed;
                    static {
                        Box.other = Box.value + 1;
                    }
                }
                yield Box.value + "|" + Box.other;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41|42:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationMixedStaticFieldAndBlockFunctionDeclaration_RoutesResumableAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static value = current;
                    static {
                        function readLater() { return current + Box.value; }
                        Box.readLater = readLater;
                    }
                }
                current = seed + 1;
                yield Box.readLater() + "|" + Box.value;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|83|41:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readLater",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationMixedStaticFieldAndBlockNestedClassDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                class Box {
                    static value = seed;
                    static {
                        class Nested {
                            static value() { return 7; }
                        }
                        Box.Nested = Nested;
                    }
                }
                yield Box;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            var Box = second.value;
            first.value + ":" + first.done + "|" + Box.value + ":" + Box.Nested.value() + ":" + second.done;
            """);

        Assert.Equal("ready:false|41:7:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationMixedStaticFieldAndBlockNestedClassDeclarationCapturingActivation_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                class Box {
                    static value = current;
                    static {
                        class Nested {
                            static value() { return current + Box.value; }
                        }
                        Box.Nested = Nested;
                    }
                }
                current = seed + 1;
                yield Box;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            var Box = second.value;
            first.value + ":" + first.done + "|" + Box.Nested.value() + ":" + Box.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|83:41:false", result);
        AssertGeneratorFastPath("g", argc: 1);
        var logs = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path static-block",
                StringComparison.Ordinal));
        Assert.Contains(
            logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            static record => record.Message.Contains(
                "classified-static-block-ir-fallback",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncClassDeclarationAfterAwait_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run(seed) {
                await 0;
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(seed);
                return box.value;
            }

            run(5).then(value => output = value);
            output;
            """);

        Assert.Equal(5d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorClassDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(seed) {
                yield "ready";
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(seed);
                yield box.value;
            }

            async function run() {
                var iterator = values(8);
                var first = await iterator.next();
                var second = await iterator.next();
                var third = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done + "|" +
                    String(third.value) + ":" + third.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("ready:false|8:false|undefined:true", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    private void AssertGeneratorFastPath(string functionName, int argc)
    {
        var snapshot = CurrentLogger!.Collector.Snapshot();
        var expected = $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}";
        var routeLogs = string.Join(
            "\n",
            snapshot
                .Select(static record => record.Message)
                .Where(static message =>
                    message.Contains("unified-bytecode", StringComparison.Ordinal) ||
                    message.Contains("classified", StringComparison.Ordinal)));
        Assert.True(
            snapshot.Any(record => record.Message.Contains(expected, StringComparison.Ordinal)),
            routeLogs);
    }

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

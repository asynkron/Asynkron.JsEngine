using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Boundary pins for the resumable nested-function tier (B23 nested function LITERAL + B36 hoisted nested
///     FUNCTION DECLARATION inside a generator/async body). Non-capturing nested functions now route through
///     the resumable VM. Generator nested literals that capture root body locals now route through a
///     materialized body environment that aliases closure captures with flat slots. Direct root hoisted
///     function declarations now route when the declared helper does not capture the resumable activation;
///     recursive/sibling helper graphs use the same pre-populated declaration environment; sync-generator,
///     async-function, and async-generator declarations that capture root body locals route through a
///     materialized body environment. Nested arrows that need lexical `this` / private-name scopes route after
///     the resumable invoker creates a per-call invocation environment for the literal to capture.
///
///     Why declined (architecture, not a missing handler):
///
///     B23 — a nested function literal that does not close over the resumable activation can be created with
///     the captured outer environment and stored in a flat slot. The first generator-only captured-local slice
///     materializes a body environment that mirrors root activation slots so captured closures observe slot
///     mutations across suspension. Async functions and async generators now share that materialized
///     environment. Lexical-this/private-name literals route when the invoker proves and materializes the
///     per-call invocation context. Function declarations nested inside literals route as well: the outer
///     resumable VM creates the literal, and the literal's normal invocation path owns declaration
///     instantiation while capturing the materialized resumable body environment when needed.
///
///     B36 — direct root hoisted function declarations are materialised by the resumable invokers before
///     `ExecuteResumable` starts. Recursive and sibling helper references resolve at call time from that same
///     pre-populated declaration environment. Sync-generator, async-function, and async-generator paths also
///     admit helpers that capture root body locals once they own a materialized body environment that mirrors
///     flat-slot state across suspension. This slice is intentionally narrow: dynamic scope and block-declaration
///     runtime semantics remain declined.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableNestedFunctionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    // B23: a non-capturing nested function literal routes through the resumable fast path.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedFunctionLiteral_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ var h = function(a){ return a*2; }; yield h(3); }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(6d, result);
        AssertGeneratorRouted();
    }

    // B23 captured-local generator slice: the nested arrow captures a generator local and observes the slot
    // mutation after the first yield through the materialized resumable body environment.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedArrowCapturesLocalAcrossYields_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ var n=1; var f=()=>n; yield f(); n=2; yield f(); }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorRouted();
    }

    // B36: a generator with a direct root hoisted function declaration routes once invocation setup
    // pre-populates the declaration slot.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(5d, result);
        AssertGeneratorRouted();
    }

    // B36 hoisting proof: a CALL textually BEFORE the declaration routes and observes the hoisted value.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclaration_CalledBeforeTextualDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){ yield helper(); function helper(){ return 7; } }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(7d, result);
        AssertGeneratorRouted();
    }

    // B36 generator captured-helper slice: a hoisted declaration that captures a resumable body local uses
    // the generator-owned materialized body environment and observes flat-slot mutations across suspension.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationCapturesLocal_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                var n = 3;
                function helper(){ return n; }
                yield helper();
                n = 4;
                yield helper();
            }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("3|4", result);
        AssertGeneratorRouted();
    }

    // B36 hoisting proof: recursive helper graphs use the pre-populated declaration environment.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationRecursiveHelper_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                function helper(n){ return n === 0 ? 1 : n * helper(n - 1); }
                yield helper(4);
            }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(24d, result);
        AssertGeneratorRouted();
    }

    // B36 hoisting proof: sibling helper references resolve after all root declarations are pre-populated.
    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationSiblingHelper_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                function helper(){ return other() + 1; }
                function other(){ return 2; }
                yield helper();
            }
            var it = g();
            it.next().value;
            """);

        Assert.Equal(3d, result);
        AssertGeneratorRouted();
    }

    // B23/B36 overlap: a nested function literal can contain its own hoisted declaration. The outer
    // resumable VM only creates the literal; the literal's normal invocation path owns its declaration
    // instantiation, while the materialized resumable body environment supplies captured outer slots.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedFunctionLiteralWithInnerDeclarationCapturingLocal_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                var n = 1;
                var f = function(){
                    function inner(){ return n; }
                    return inner();
                };
                yield f();
                n = 2;
                yield f();
            }
            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorRouted();
    }

    // B32 boundary: external .return()/.throw() while suspended in a protected try must run the pending
    // finally. The resumable VM does not yet drive captured/free plain writes in that early-close cleanup,
    // so this shape stays on the IR runner.
    [Fact(Timeout = 5000)]
    public async Task GeneratorTryFinallyMutatesOuterBindingAfterYield_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let cleanupCalled = false;
            function* g(){
                try {
                    yield 1;
                } finally {
                    cleanupCalled = true;
                }
            }

            var it = g();
            it.next();
            var closed = it.return(99);
            cleanupCalled + "|" + closed.value + "|" + closed.done;
            """);

        Assert.Equal("true|99|true", result);
        AssertGeneratorNotRouted();
    }

    // B23 proof: nested arrows that read lexical this/private names route once the resumable invocation
    // environment owns the per-call this binding and private-name scopes.
    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedArrowUsesPrivateThis_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                class Counter {
                    #count = 0;

                    *counter() {
                        const inc = () => ++this.#count;
                        yield inc();
                        yield inc();
                        yield inc();
                    }
                }

                const iterator = new Counter().counter();
                const r1 = iterator.next().value;
                const r2 = iterator.next().value;
                const r3 = iterator.next().value;
                return r1 === 1 && r2 === 2 && r3 === 3;
            })();
            """);

        Assert.True((bool)result!);
        AssertGeneratorRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorNestedArrowCapturesCallThis_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                const read = () => this.value;
                yield read();
                this.value = 2;
                yield read();
            }

            const receiver = { value: 1 };
            const iterator = g.call(receiver);
            const first = iterator.next().value;
            const second = iterator.next().value;
            first + "|" + second + "|" + receiver.value;
            """);

        Assert.Equal("1|2|2", result);
        AssertGeneratorRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncHoistedFunctionDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                await p;
                return helper();
                function helper(){ return 9; }
            }
            run(Promise.resolve(0)).then(v => done = v, e => done = String(e));
            done;
            """);

        Assert.Equal(9d, result);
        AssertAsyncRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorHoistedFunctionDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function* values(){
                function helper(){ return 11; }
                yield helper();
            }
            async function run(){
                var it = values();
                var first = await it.next();
                return first.value + ":" + first.done;
            }
            run().then(v => done = v);
            done;
            """);

        Assert.Equal("11:false", result);
        AssertAsyncGeneratorRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncHoistedFunctionDeclarationCapturesLocalAndCallsInsideBody_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                var n = 12;
                function helper(){ return n; }
                await p;
                n = 13;
                return helper();
            }
            run(Promise.resolve(0)).then(v => done = v, e => done = String(e));
            done;
            """);

        Assert.Equal(13d, result);
        AssertAsyncRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorHoistedFunctionDeclarationCapturesLocal_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function* values(p){
                var n = 13;
                function helper(){ return n; }
                await p;
                yield helper();
                n = 14;
                yield helper();
            }
            async function run(){
                var it = values(Promise.resolve(0));
                var a = await it.next();
                var b = await it.next();
                return a.value + "|" + b.value;
            }
            run().then(v => done = v);
            done;
            """);

        Assert.Equal("13|14", result);
        AssertAsyncGeneratorRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorNestedArrowCapturesLocalAcrossYields_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function* values(p){
                var n = 1;
                var read = () => n;
                await p;
                yield read();
                n = 2;
                yield read();
            }
            async function run(){
                var it = values(Promise.resolve(0));
                var a = await it.next();
                var b = await it.next();
                return a.value + "|" + b.value;
            }
            run().then(v => done = v);
            done;
            """);

        Assert.Equal("1|2", result);
        AssertAsyncGeneratorRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationWithRuntimeSourceEval_DeclinesExplicitly()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => engine.Evaluate("""
            function* g(source){
                function helper(){ return 5; }
                yield eval(source);
                yield helper();
            }
            var it = g('1 + 41');
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """));

        Assert.StartsWith(
            "Sync-generator body 'g' is not eligible for unified bytecode execution:",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("Root hoisted function declarations", exception.Message, StringComparison.Ordinal);
        AssertGeneratorNotRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorHoistedFunctionDeclarationWithArgumentsEval_DeclinesExplicitly()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => engine.Evaluate("""
            function* g(first, second){
                function helper(){ return 5; }
                yield eval('arguments.length + ":" + first + ":" + second');
                yield helper();
            }
            var it = g(7, 9);
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """));

        Assert.StartsWith(
            "Sync-generator body 'g' is not eligible for unified bytecode execution:",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("Root hoisted function declarations", exception.Message, StringComparison.Ordinal);
        AssertGeneratorNotRouted();
    }

    // B23 async variant: an async function materializes and name-infers a non-capturing nested function
    // literal after an await and routes through the resumable fast path. Calling that literal inside a larger
    // return expression remains a separate call-boundary gate.
    [Fact(Timeout = 5000)]
    public async Task AsyncNestedFunctionLiteral_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                await p;
                var dbl = function(a){ return a*2; };
                return typeof dbl + "|" + dbl.name;
            }
            run(Promise.resolve(0)).then(v => done = v);
            done;
            """);

        Assert.Equal("function|dbl", result);
        AssertAsyncRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncNestedArrowCapturesLocalAcrossAwaits_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                var n = 1;
                var read = () => n;
                await p;
                n = 2;
                return read;
            }
            run(Promise.resolve(0)).then(fn => done = fn());
            done;
            """);

        Assert.Equal(2d, result);
        AssertAsyncRouted();
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncNestedArrowCapturesLocalAndCallsInsideBody_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(p){
                var n = 1;
                var read = () => n;
                await p;
                n = 2;
                return read();
            }
            run(Promise.resolve(0)).then(v => done = v);
            done;
            """);

        Assert.Equal(2d, result);
        AssertAsyncRouted();
    }

    // Eligibility gate: non-capturing nested function literals are now admitted and compile to LoadFunctionLiteral.
    [Fact]
    public void EvaluateResumable_NestedFunctionLiteral_AdmitsLoadFunctionLiteralOpcode()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var h = function(a){ return a*2; }; yield h(3); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncNestedFunctionLiteral_AdmitsLoadFunctionLiteralOpcode()
    {
        var plan = TopLevelGeneratorPlan("""
            async function run(p){
                await p;
                var dbl = function(a){ return a*2; };
                return typeof dbl + "|" + dbl.name;
            }
            """, "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnsureHasName);
    }

    // Eligibility gate: root hoisted function declarations admit only when the invoker has proven that it
    // can pre-populate the declaration slots. Plain plan-only checks still decline.
    [Fact]
    public void EvaluateResumable_HoistedFunctionDeclaration_DeclinesWithoutActivationProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("FunctionDeclarationInstruction", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_HoistedFunctionDeclaration_AdmitsWithActivationProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ function helper(){ return 5; } yield helper(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsRootFunctionDeclarationInstructions: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.DoesNotContain(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareFunction);
    }

    [Fact]
    public void EvaluateResumable_HoistedFunctionDeclarationHelperGraph_AdmitsWithActivationProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){
                function helper(n){ return n === 0 ? other() : n * helper(n - 1); }
                function other(){ return 1; }
                yield helper(4);
            }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsRootFunctionDeclarationInstructions: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.DoesNotContain(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareFunction);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralCapturingLocal_StillDeclines()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var n=1; var f=()=>n; yield f(); n=2; yield f(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("captures activation binding 'n'", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralCapturingLocal_AdmitsWithMaterializedBodyEnvironmentProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var n=1; var f=()=>n; yield f(); n=2; yield f(); }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralCapturingPerIterationBinding_Declines()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(values) {
                for (const v of values) {
                    yield () => v;
                }
            }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("scoped binding 'v'", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_NestedArrowLexicalThis_AdmitsWithContextProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){ var f=()=>this.value; yield f(); }
            """, "g");

        var resultWithoutProof = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(resultWithoutProof.IsEligible);
        Assert.Contains("lexical this/private name", resultWithoutProof.Reason, StringComparison.Ordinal);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsNestedFunctionLiteralLexicalThisOrPrivateNameContext: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncNestedFunctionLiteralCapturingLocal_AdmitsWithMaterializedBodyEnvironmentProof()
    {
        var plan = TopLevelGeneratorPlan("""
            async function run(p){
                var n = 1;
                var f = () => n;
                await p;
                return f();
            }
            """, "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: true,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorNestedFunctionLiteralCapturingLocal_AdmitsWithMaterializedBodyEnvironmentProof()
    {
        var plan = TopLevelGeneratorPlan("""
            async function* values(p){
                var n = 1;
                var f = () => n;
                await p;
                yield f();
            }
            """, "values");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: true,
                IsGenerator: true,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralWithInnerDeclarationCapturingLocal_DeclinesWithoutEnvironmentProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){
                var n = 1;
                var f = function(){
                    function inner(){ return n; }
                    return inner();
                };
                yield f();
            }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("captures activation binding 'n'", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_NestedFunctionLiteralWithInnerDeclarationCapturingLocal_AdmitsWithMaterializedBodyEnvironmentProof()
    {
        var plan = TopLevelGeneratorPlan("""
            function* g(){
                var n = 1;
                var f = function(){
                    function inner(){ return n; }
                    return inner();
                };
                yield f();
            }
            """, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                IsGenerator: true,
                AllowsMaterializedBodyEnvironmentFunctionLiterals: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadFunctionLiteral);
    }

    private void AssertGeneratorRouted() =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncRouted() =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncGeneratorRouted() =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertGeneratorNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncGeneratorNotRouted() =>
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncGeneratorFastPathLog, StringComparison.Ordinal));

    private static ExecutionPlan TopLevelGeneratorPlan(string source, string name)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == name));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}

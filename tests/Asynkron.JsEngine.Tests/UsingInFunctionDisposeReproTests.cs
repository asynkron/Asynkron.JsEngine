using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Regression tests for explicit-resource-management ('using' / 'await using')
/// disposal when the declaration lives at a function-body top-level scope.
/// Function bodies bind their top-level lexical declarations directly in the
/// execution (function) environment, which has no enclosing
/// PushEnvironment/PopEnvironment to trigger disposal — so disposal must fire on
/// function completion (normal return, throw, break/continue out of an inner block).
/// </summary>
public sealed class UsingInFunctionDisposeReproTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    [Fact(Timeout = 5000)]
    public async Task TopLevel_Using_Disposes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            { using a = mk('d'); log.push('b'); }
            log.join(',');
        ");
        Assert.Equal("b,d", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_Disposes_OnNormalReturn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){ using a = mk('d'); log.push('b'); }
            f();
            log.join(',');
        ");
        Assert.Equal("b,d", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_Disposes_OnExplicitReturn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){ using a = mk('d'); log.push('b'); return 42; }
            var r = f();
            log.push('r' + r);
            log.join(',');
        ");
        Assert.Equal("b,d,r42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_NestedBlockUsing_DisposesAllActiveScopes_OnExplicitReturn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){
                using outer = mk('outer');
                {
                    using inner = mk('inner');
                    return 'body';
                }
            }
            var r = f();
            log.push('return:' + r);
            log.join(',');
        ");
        Assert.Equal("inner,outer,return:body", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_Disposes_OnThrow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){ using a = mk('d'); log.push('b'); throw new Error('boom'); }
            try { f(); } catch (e) { log.push('caught:' + e.message); }
            log.join(',');
        ");
        Assert.Equal("b,d,caught:boom", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_MultipleResources_DisposeInLifoOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){
                using a = mk('a');
                using b = mk('b');
                using c = mk('c');
                log.push('body');
            }
            f();
            log.join(',');
        ");
        // Declaration order a,b,c -> disposal LIFO c,b,a
        Assert.Equal("body,c,b,a", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_Disposes_OnBreakOutOfInnerLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){
                using a = mk('d');
                for (let i = 0; i < 3; i++) {
                    log.push('i' + i);
                    if (i === 1) break;
                }
                log.push('after');
            }
            f();
            log.join(',');
        ");
        Assert.Equal("i0,i1,after,d", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_Disposes_OnContinueInInnerLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function f(){
                using a = mk('d');
                for (let i = 0; i < 3; i++) {
                    if (i === 1) continue;
                    log.push('i' + i);
                }
                log.push('after');
            }
            f();
            log.join(',');
        ");
        Assert.Equal("i0,i2,after,d", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InFunction_Using_DisposeThrows_WrapsOriginalInSuppressedError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function f(){
                using a = { [Symbol.dispose](){ throw new Error('disposeFail'); } };
                throw new Error('bodyFail');
            }
            try {
                f();
            } catch (e) {
                log.push('name:' + e.name);
                log.push('err:' + e.error.message);
                log.push('suppressed:' + e.suppressed.message);
            }
            log.join(',');
        ");
        // SuppressedError: error = dispose failure, suppressed = original body error
        Assert.Equal("name:SuppressedError,err:disposeFail,suppressed:bodyFail", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InNestedFunction_Using_Disposes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            function outer(){
                using o = mk('outer');
                function inner(){
                    using i = mk('inner');
                    log.push('inner-body');
                }
                inner();
                log.push('outer-body');
            }
            outer();
            log.join(',');
        ");
        Assert.Equal("inner-body,inner,outer-body,outer", result);
    }

    [Fact(Timeout = 5000)]
    public async Task InAsyncFunction_AwaitUsing_Disposes_OnNormalReturn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { async [Symbol.asyncDispose](){ log.push(t); } }; }
            async function f(){ await using a = mk('d'); log.push('b'); }
            f().then(() => log.push('done'));
        ");
        // Drain the microtask queue
        await engine.Evaluate("log;");
        var joined = await engine.Evaluate("log.join(',');");
        Assert.Equal("b,d,done", joined);
    }

    [Fact(Timeout = 5000)]
    public async Task InAsyncFunction_Using_Disposes_OnNormalReturn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            async function f(){ using a = mk('d'); log.push('b'); }
            f().then(() => log.push('done'));
        ");
        await engine.Evaluate("log;");
        var joined = await engine.Evaluate("log.join(',');");
        Assert.Equal("b,d,done", joined);
    }

    [Fact(Timeout = 5000)]
    public async Task InAsyncFunction_Using_Disposes_OnThrow()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate(@"
            var log=[];
            function mk(t){ return { [Symbol.dispose](){ log.push(t); } }; }
            async function f(){ using a = mk('d'); log.push('b'); throw new Error('boom'); }
            f().catch((e) => log.push('caught:' + e.message));
        ");
        await engine.Evaluate("log;");
        var joined = await engine.Evaluate("log.join(',');");
        Assert.Equal("b,d,caught:boom", joined);
    }
}

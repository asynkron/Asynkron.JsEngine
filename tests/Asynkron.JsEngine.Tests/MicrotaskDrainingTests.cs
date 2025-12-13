namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for microtask draining behavior.
///
/// NOTE: Unlike Jint which drains microtasks before returning from Evaluate(),
/// Asynkron follows standard JavaScript/Node.js semantics where microtasks run
/// after the synchronous code completes. This means:
///
/// - `Promise.resolve().then(() => { x = 1; }); return x;` returns the OLD value
/// - The microtask callback runs AFTER the script completes, not before
///
/// This matches Node.js behavior:
///   node -e "let y=0; (async()=>{y=await Promise.resolve(42)})(); console.log(y)"
///   Output: 0 (not 42)
/// </summary>
public class MicrotaskDrainingTests
{
    [Fact(Timeout = 5000)]
    public async Task MicrotasksRunAfterScriptCompletion_NotDuring()
    {
        // This test verifies that we follow ECMAScript semantics:
        // Microtasks run AFTER the current synchronous execution, not during.
        await using var engine = new JsEngine();

        // First, execute code that schedules a microtask
        var result = await engine.Evaluate("""
            let finalResult = 0;
            Promise.resolve(42).then(x => { finalResult = x; });
            finalResult;  // Returns 0 because microtask hasn't run yet
            """);

        // Per ECMAScript spec, the return value should be 0
        // because the .then() callback runs after the script completes
        Assert.Equal(0.0, result);

        // But after the script completes, microtasks should drain
        // and the variable should be updated
        var afterResult = await engine.Evaluate("finalResult");
        Assert.Equal(42.0, afterResult);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncIIFE_ReturnValueIsPreAsyncCompletion()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            let finalResult = 0;
            (async function() {
                finalResult = await Promise.resolve(42);
            })();
            finalResult;  // Returns 0, async work not done yet
            """);

        // Same as Node.js - returns 0 because async work hasn't completed
        Assert.Equal(0.0, result);

        // After the script, the async work completes
        var afterResult = await engine.Evaluate("finalResult");
        Assert.Equal(42.0, afterResult);
    }

    [Fact(Timeout = 5000)]
    public async Task MultipleScripts_MicrotasksDrainBetweenThem()
    {
        await using var engine = new JsEngine();

        // First script: schedule a microtask
        await engine.Evaluate("""
            let result = 0;
            Promise.resolve(42).then(x => { result = x; });
            """);

        // Second script: microtask should have drained between scripts
        var result = await engine.Evaluate("result");
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedPromises_AllResolveBeforeNextScript()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            let finalResult = 0;
            Promise.resolve(1)
                .then(x => x + 1)
                .then(x => x + 1)
                .then(x => x + 1)
                .then(x => { finalResult = x; });
            """);

        // By the next script, all microtasks should have drained
        var result = await engine.Evaluate("finalResult");
        Assert.Equal(4.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionCall_CompletesBeforeNextScript()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            let finalResult = 0;
            async function asyncAdd(a, b) {
                return a + b;
            }
            (async function() {
                let result = 0;
                for (let i = 0; i < 10; i++) {
                    result = await asyncAdd(result, i);
                }
                finalResult = result;
            })();
            """);

        // By the next script, the async function should have completed
        var result = await engine.Evaluate("finalResult");
        // 0+1+2+3+4+5+6+7+8+9 = 45
        Assert.Equal(45.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task PromiseConstructor_ResolvesBeforeNextScript()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            let finalResult = 0;
            function makePromise(val) {
                return new Promise(resolve => resolve(val));
            }
            (async function() {
                finalResult = await makePromise(42);
            })();
            """);

        var result = await engine.Evaluate("finalResult");
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task EvaluateAndAwait_ReturnsValueAfterMicrotasksDrain()
    {
        // EvaluateAndAwait is a convenience method that returns the value of
        // the trailing identifier AFTER microtasks have drained (like Jint).
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalResult = 0;
            Promise.resolve(42).then(x => { finalResult = x; });
            finalResult;
            """);

        // EvaluateAndAwait should return 42 (the value after microtasks drain)
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task EvaluateAndAwait_AsyncIIFE_ReturnsResolvedValue()
    {
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalResult = 0;
            (async function() {
                finalResult = await Promise.resolve(42);
            })();
            finalResult;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task EvaluateAndAwait_NestedPromises_ReturnsResolvedValue()
    {
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalResult = 0;
            Promise.resolve(1)
                .then(x => x + 1)
                .then(x => x + 1)
                .then(x => x + 1)
                .then(x => { finalResult = x; });
            finalResult;
            """);

        Assert.Equal(4.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task EvaluateAndAwait_NoTrailingIdentifier_ReturnsNormally()
    {
        // When there's no trailing identifier, EvaluateAndAwait behaves like Evaluate
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let x = 1 + 2;
            x * 2;
            """);

        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task EvaluateAndAwait_ComplexAsyncLoop_ReturnsResolvedValue()
    {
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalResult = 0;
            async function asyncAdd(a, b) {
                return a + b;
            }
            (async function() {
                let result = 0;
                for (let i = 0; i < 10; i++) {
                    result = await asyncAdd(result, i);
                }
                finalResult = result;
            })();
            finalResult;
            """);

        // 0+1+2+3+4+5+6+7+8+9 = 45
        Assert.Equal(45.0, result);
    }
}

/// <summary>
/// Tests demonstrating the for await...of bug where async iteration
/// doesn't complete when used inside an async IIFE.
/// However, for await...of DOES work correctly with top-level await in ES modules.
/// </summary>
public class ForAwaitOfBugTests
{
    [Fact(Timeout = 5000, Skip = "Known bug: for await...of in IIFE doesn't drain correctly")]
    public async Task ForAwaitOf_InIIFE_DoesNotComplete()
    {
        // BUG: for await...of inside an async IIFE doesn't complete
        // within a single Evaluate call, even after microtasks drain.
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            const arr = [1, 2, 3, 4, 5];
            (async function() {
                let sum = 0;
                for await (const n of arr) {
                    sum += n;
                }
                finalSum = sum;
            })();
            finalSum;
            """);

        // Expected: 15 (1+2+3+4+5)
        // Actual: 0 (async iteration hasn't completed)
        Assert.Equal(15.0, result);
    }

    [Fact(Timeout = 5000, Skip = "Known bug: for await...of in IIFE doesn't drain correctly")]
    public async Task ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete()
    {
        // BUG: for await...of in IIFE doesn't complete even with multiple iterations
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            (async function() {
                let sum = 0;
                for (let i = 0; i < 5; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
                finalSum = sum;
            })();
            finalSum;
            """);

        // Expected: 275 (55 * 5)
        // Actual: 0 (async iteration hasn't completed)
        Assert.Equal(275.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_TopLevelInModule_WorksCorrectly()
    {
        // WORKS: for await...of at top-level in an ES module completes correctly
        await using var engine = new JsEngine();

        var result = await engine.EvaluateModule("""
            const arr = [1, 2, 3, 4, 5];
            let sum = 0;
            for await (const n of arr) {
                sum += n;
            }
            sum;
            """);

        // Top-level await in modules works correctly
        Assert.Equal(15.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_TopLevelInModule_MultipleIterations_WorksCorrectly()
    {
        // WORKS: for await...of at top-level with multiple iterations
        await using var engine = new JsEngine();

        var result = await engine.EvaluateModule("""
            const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            let sum = 0;
            for (let i = 0; i < 5; i++) {
                for await (const n of arr) {
                    sum += n;
                }
            }
            sum;
            """);

        // Expected: 275 (55 * 5)
        Assert.Equal(275.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task RegularAwaitInLoop_InIIFE_WorksCorrectly()
    {
        // COMPARISON: Regular await inside a for loop in IIFE DOES work correctly
        // This shows the issue is specific to for await...of in IIFEs
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            const arr = [1, 2, 3, 4, 5];
            (async function() {
                let sum = 0;
                for (let i = 0; i < arr.length; i++) {
                    sum += await Promise.resolve(arr[i]);
                }
                finalSum = sum;
            })();
            finalSum;
            """);

        // This works correctly - regular await in a loop completes
        Assert.Equal(15.0, result);
    }
}

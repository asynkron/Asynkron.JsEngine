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
    [Fact(Timeout = 5000)]
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

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete2()
    {
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
                                                   'use strict'
                                                   var finalSum = 0;
                                                   const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
                                                   (async function() {
                                                       let sum = 0;
                                                       for (let i = 0; i < 5000; i++) {
                                                           for await (const n of arr) {
                                                          sum += n;
                                                           }
                                                       }
                                                       finalSum = sum;
                                                   })();
                                                   finalSum;
                                                   """);

        // Expected: 275 (55 * 5000)
        Assert.Equal(275000.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_InIIFE_MultipleIterations_DoesNotComplete()
    {
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
    public async Task NestedForAwaitOf_MinimalCase_TwoOuterIterations()
    {
        // Minimal reproduction case for nested for-await-of
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
            })();
            sum;
            """);

        // Expected: 6 = (1+2) + (1+2)
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedForAwaitOf_TenOuterIterations()
    {
        // Test with 10 outer iterations
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2, 3];
                for (let i = 0; i < 10; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
            })();
            sum;
            """);

        // Expected: 60 = 6 * 10
        Assert.Equal(60.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedForAwaitOf_HundredOuterIterations()
    {
        // Test with 100 outer iterations
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2, 3];
                for (let i = 0; i < 100; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
            })();
            sum;
            """);

        // Expected: 600 = 6 * 100
        Assert.Equal(600.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedForAwaitOf_WithVarBinding()
    {
        // Test with var binding in outer loop (different scoping)
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (var i = 0; i < 3; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
            })();
            sum;
            """);

        // Expected: 9 = 3 * 3
        Assert.Equal(9.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedForAwaitOf_WhileOuter()
    {
        // Test with while loop as outer loop
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                let i = 0;
                while (i < 3) {
                    for await (const n of arr) {
                        sum += n;
                    }
                    i++;
                }
            })();
            sum;
            """);

        // Expected: 9 = 3 * 3
        Assert.Equal(9.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedForAwaitOf_TripleNested()
    {
        // Test with three levels of nesting
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 12 = 2 * 2 * (1+2)
        Assert.Equal(12.0, result);
    }

    // ===========================================
    // Comprehensive Nested Loop Tests
    // ===========================================

    [Fact(Timeout = 5000)]
    public async Task TripleNestedForLoops_SyncOnly_WithLet()
    {
        // 3 nested regular for loops with let - no async at all
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            for (let i = 0; i < 2; i++) {
                for (let j = 0; j < 2; j++) {
                    for (let k = 0; k < 3; k++) {
                        sum += 1;
                    }
                }
            }
            sum;
            """);

        // Expected: 2 * 2 * 3 = 12
        Assert.Equal(12.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TripleNestedForAwaitOf_AllAsync()
    {
        // 3 nested for-await-of loops
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr1 = [1];
                const arr2 = [1];
                const arr3 = [1, 2, 3];
                for await (const a of arr1) {
                    for await (const b of arr2) {
                        for await (const c of arr3) {
                            sum += c;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 1 * 1 * (1+2+3) = 6
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TripleNestedForAwaitOf_AllAsync_2x2x2()
    {
        // 3 nested for-await-of loops with 2 elements each
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for await (const a of arr) {
                    for await (const b of arr) {
                        for await (const c of arr) {
                            sum += 1;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * 2 = 8
        Assert.Equal(8.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_For_ForAwaitOf_Mixed()
    {
        // for-await-of, then for, then for-await-of
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for await (const a of arr) {
                    for (let j = 0; j < 2; j++) {
                        for await (const c of arr) {
                            sum += 1;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * 2 = 8
        Assert.Equal(8.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task For_ForAwaitOf_For_Mixed()
    {
        // for, then for-await-of, then for
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for await (const b of arr) {
                        for (let k = 0; k < 2; k++) {
                            sum += 1;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * 2 = 8
        Assert.Equal(8.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task DoubleNestedFor_ForAwaitOf_Inner()
    {
        // 2 nested for loops, then for-await-of innermost
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2, 3];
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * (1+2+3) = 24
        Assert.Equal(24.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task DoubleNestedForAwaitOf_For_Inner()
    {
        // 2 nested for-await-of, then for innermost
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for await (const a of arr) {
                    for await (const b of arr) {
                        for (let k = 0; k < 3; k++) {
                            sum += 1;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * 3 = 12
        Assert.Equal(12.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task For_DoubleNestedForAwaitOf()
    {
        // for outer, then 2 nested for-await-of
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for await (const b of arr) {
                        for await (const c of arr) {
                            sum += 1;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * 2 = 8
        Assert.Equal(8.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_DoubleNestedFor()
    {
        // for-await-of outer, then 2 nested for loops
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for await (const a of arr) {
                    for (let j = 0; j < 2; j++) {
                        for (let k = 0; k < 3; k++) {
                            sum += 1;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * 3 = 12
        Assert.Equal(12.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TripleNestedFor_WithVar_NoPerIterationEnv()
    {
        // 3 nested for loops with var - should NOT create per-iteration envs
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (var i = 0; i < 2; i++) {
                    for (var j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * (1+2) = 12
        Assert.Equal(12.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedFor_MixedLetVar_OuterVar()
    {
        // Outer var, middle let, inner for-await-of
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (var i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * (1+2) = 12
        Assert.Equal(12.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedFor_MixedLetVar_MiddleVar()
    {
        // Outer let, middle var, inner for-await-of
        await using var engine = new JsEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for (var j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        // Expected: 2 * 2 * (1+2) = 12
        Assert.Equal(12.0, result);
    }

    // ===========================================
    // End Comprehensive Nested Loop Tests
    // ===========================================

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

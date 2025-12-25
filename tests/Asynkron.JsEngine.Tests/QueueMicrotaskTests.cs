namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for the queueMicrotask global function.
/// queueMicrotask schedules a callback to run as a microtask, after the current
/// synchronous code completes but before the next macrotask (like setTimeout).
/// </summary>
public class QueueMicrotaskTests
{
    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_ExecutesCallback()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            let executed = false;
            queueMicrotask(() => { executed = true; });
            executed;
            """);

        // Callback hasn't executed yet during synchronous code
        Assert.Equal(false, result);

        // After script completes, microtask should have drained
        var afterResult = await engine.Evaluate("executed");
        Assert.Equal(true, afterResult);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_ExecutesInOrder()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const order = [];
            queueMicrotask(() => order.push(1));
            queueMicrotask(() => order.push(2));
            queueMicrotask(() => order.push(3));
            """);

        var result = await engine.Evaluate("order.join(',')");
        Assert.Equal("1,2,3", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_MultipleCallbacksInSameScript()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const order = [];
            queueMicrotask(() => order.push('first'));
            queueMicrotask(() => order.push('second'));
            order.push('sync');
            """);

        var result = await engine.Evaluate("order.join(',')");
        // 'sync' runs first (synchronous), then microtasks drain
        Assert.Equal("sync,first,second", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_NestedMicrotasksExecuteInOrder()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const order = [];

            queueMicrotask(() => {
                order.push('first');
                queueMicrotask(() => order.push('nested'));
            });

            queueMicrotask(() => order.push('second'));
            """);

        var result = await engine.Evaluate("order.join(',')");
        // first runs, queues nested, then second runs, then nested runs
        Assert.Equal("first,second,nested", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_InterleavesWithPromises()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const order = [];

            Promise.resolve().then(() => order.push('promise1'));
            queueMicrotask(() => order.push('microtask1'));
            Promise.resolve().then(() => order.push('promise2'));
            queueMicrotask(() => order.push('microtask2'));
            """);

        var result = await engine.Evaluate("order.join(',')");
        // All microtasks (including promise reactions) should execute in order queued
        Assert.Equal("promise1,microtask1,promise2,microtask2", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_ThrowsOnNonCallable()
    {
        await using var engine = new JsEngine();

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("queueMicrotask('not a function')");
        });

        Assert.Contains("TypeError", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_ThrowsOnNoArguments()
    {
        await using var engine = new JsEngine();

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("queueMicrotask()");
        });

        Assert.Contains("TypeError", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_CallbackReceivesNoArguments()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            let argCount = -1;
            queueMicrotask(function() { argCount = arguments.length; });
            """);

        var result = await engine.Evaluate("argCount");
        Assert.Equal(0.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_ReturnsUndefined()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            typeof queueMicrotask(() => {});
            """);

        Assert.Equal("undefined", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_ExceptionInCallbackDoesNotStopOtherMicrotasks()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const order = [];
            queueMicrotask(() => order.push('first'));
            queueMicrotask(() => { throw new Error('oops'); });
            queueMicrotask(() => order.push('third'));
            """);

        // Despite the exception in the second callback, all should run
        var result = await engine.Evaluate("order.join(',')");
        Assert.Equal("first,third", result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_WorksWithArrowFunctions()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            let value = 0;
            queueMicrotask(() => value = 42);
            """);

        var result = await engine.Evaluate("value");
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task QueueMicrotask_WorksWithBoundFunctions()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const obj = { value: 0 };
            function setValueTo42() { this.value = 42; }
            queueMicrotask(setValueTo42.bind(obj));
            """);

        var result = await engine.Evaluate("obj.value");
        Assert.Equal(42.0, result);
    }
}

using System.Reflection;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.Regression)]
public sealed class PendingAsyncWorkTrackingTests
{
    [Fact(Timeout = 10000)]
    public async Task Evaluate_SynchronousProgram_CompletesWithoutStartingEventLoop()
    {
        await using var engine = new JsEngine();

        var engineType = typeof(JsEngine);
        var eventQueueField = engineType.GetField("_eventQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(eventQueueField);

        var parsed = engine.ParseProgram("let x = 1 + 2; x;");
        var task = engine.Evaluate(parsed);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Null(eventQueueField.GetValue(engine));

        var result = await task;
        var number = Assert.IsType<double>(result);
        Assert.Equal(3d, number);
    }

    [Fact(Timeout = 10000)]
    public async Task Evaluate_SynchronousThrow_PreservesPendingTimerWorkForNextDrain()
    {
        await using var engine = new JsEngine();

        var engineType = typeof(JsEngine);
        var activeTimerCountField = engineType.GetField(
            "_activeTimerCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(activeTimerCountField);

        var pendingTaskCountField = engineType.GetField(
            "_pendingTaskCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(pendingTaskCountField);

        await Assert.ThrowsAnyAsync<Exception>(async () => await engine.Evaluate("""
            globalThis.__timerObserved = 0;
            setTimeout(function() { globalThis.__timerObserved = 1; }, 0);
            throw new Error("boom");
            """));

        Assert.Equal(1, (int)activeTimerCountField.GetValue(engine)!);
        Assert.Equal(1, (int)pendingTaskCountField.GetValue(engine)!);

        await engine.Evaluate("0;");

        var observed = await engine.Evaluate("globalThis.__timerObserved;");
        Assert.Equal(1d, Assert.IsType<double>(observed));
        Assert.Equal(0, (int)activeTimerCountField.GetValue(engine)!);
        Assert.Equal(0, (int)pendingTaskCountField.GetValue(engine)!);
    }

    [Fact(Timeout = 10000)]
    public async Task Evaluate_PreCanceledToken_ReturnsCanceledTask()
    {
        await using var engine = new JsEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = engine.Evaluate("while (true) { }", cts.Token);

        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact(Timeout = 10000)]
    public async Task TrackPendingAsyncWork_CompletedTask_DoesNotStartEventLoopOrIncrementPendingCount()
    {
        await using var engine = new JsEngine();

        var engineType = typeof(JsEngine);
        var eventQueueField = engineType.GetField("_eventQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(eventQueueField);

        var pendingTaskCountField = engineType.GetField("_pendingTaskCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(pendingTaskCountField);

        var trackMethod = engineType.GetMethod("TrackPendingAsyncWork", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(trackMethod);

        Assert.Null(eventQueueField.GetValue(engine));
        Assert.Equal(0, (int)pendingTaskCountField.GetValue(engine)!);

        trackMethod.Invoke(engine, new object[] { Task.CompletedTask });

        Assert.Null(eventQueueField.GetValue(engine));
        Assert.Equal(0, (int)pendingTaskCountField.GetValue(engine)!);
    }
}

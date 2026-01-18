using System.Reflection;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.Regression)]
public sealed class PendingAsyncWorkTrackingTests
{
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

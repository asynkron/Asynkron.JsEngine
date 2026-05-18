using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
public sealed class EventQueueTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Run_ExecutesCodeAndReturnsResult()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("2 + 3;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_ProcessesScheduledTasks()
    {
        await using var engine = CreateEngine();
        var executed = false;

        // Schedule a task before running code
        engine.ScheduleTask(() => executed = true);

        await engine.Evaluate("1 + 1;");

        Assert.True(executed, "Scheduled task should have been executed");
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Run_ProcessesMultipleScheduledTasks()
    {
        await using var engine = CreateEngine();
        var executionOrder = new List<int>();

        engine.ScheduleTask(() => executionOrder.Add(1));
        engine.ScheduleTask(() => executionOrder.Add(2));
        engine.ScheduleTask(() => executionOrder.Add(3));

        await engine.Evaluate("let x = 42;");

        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_ProcessesTasksScheduledDuringExecution()
    {
        await using var engine = CreateEngine();
        var executionOrder = new List<int>();

        // Schedule a task that schedules another task
        engine.ScheduleTask(() =>
        {
            executionOrder.Add(1);
            engine.ScheduleTask(() => executionOrder.Add(2));
        });

        await engine.Evaluate("let x = 1;");

        Assert.Equal(new[] { 1, 2 }, executionOrder);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_CompletesWhenQueueIsEmpty()
    {
        await using var engine = CreateEngine();

        // Run with no scheduled tasks - should complete immediately
        var result = await engine.Evaluate("5 + 5;");

        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScheduleTask_CanBeCalledMultipleTimes()
    {
        await using var engine = CreateEngine();
        var count = 0;

        for (var i = 0; i < 10; i++)
        {
            engine.ScheduleTask(() => count++);
        }

        await engine.Evaluate("let x = 1;");

        Assert.Equal(10, count);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_AllowsInteractionWithHostFunctions()
    {
        await using var engine = CreateEngine();
        var capturedValues = new List<JsValue>();
        var capturedValuesLock = new object();

        engine.SetGlobalFunction("capture", args =>
        {
            lock (capturedValuesLock)
            {
                capturedValues.AddRange(args);
            }

            return JsValue.Null;
        });

        engine.ScheduleTask(() =>
        {
            lock (capturedValuesLock)
            {
                capturedValues.Add("from-task");
            }
        });

        await engine.Evaluate("capture(1, 2, 3);");

        JsValue[] snapshot;
        lock (capturedValuesLock)
        {
            snapshot = capturedValues.ToArray();
        }

        Assert.Contains(1d, snapshot);
        Assert.Contains(2d, snapshot);
        Assert.Contains(3d, snapshot);
        Assert.Contains("from-task", snapshot);
    }
}

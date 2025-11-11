using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class EventQueueTests
{
    [Fact(Timeout = 2000)]
    public async Task Run_ExecutesCodeAndReturnsResult()
    {
        var engine = new JsEngine();
        var result = await engine.Run("2 + 3;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_ProcessesScheduledTasks()
    {
        var engine = new JsEngine();
        var executed = false;

        // Schedule a task before running code
        engine.ScheduleTask(() =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        await engine.Run("1 + 1;");

        Assert.True(executed, "Scheduled task should have been executed");
    }

    [Fact(Timeout = 2000)]
    public async Task Run_ProcessesMultipleScheduledTasks()
    {
        var engine = new JsEngine();
        var executionOrder = new List<int>();

        engine.ScheduleTask(() =>
        {
            executionOrder.Add(1);
            return Task.CompletedTask;
        });

        engine.ScheduleTask(() =>
        {
            executionOrder.Add(2);
            return Task.CompletedTask;
        });

        engine.ScheduleTask(() =>
        {
            executionOrder.Add(3);
            return Task.CompletedTask;
        });

        await engine.Run("let x = 42;");

        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_ProcessesTasksScheduledDuringExecution()
    {
        var engine = new JsEngine();
        var executionOrder = new List<int>();

        // Schedule a task that schedules another task
        engine.ScheduleTask(() =>
        {
            executionOrder.Add(1);
            engine.ScheduleTask(() =>
            {
                executionOrder.Add(2);
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        await engine.Run("let x = 1;");

        Assert.Equal(new[] { 1, 2 }, executionOrder);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_HandlesAsyncTasks()
    {
        var engine = new JsEngine();
        var executed = false;

        engine.ScheduleTask(async () =>
        {
            await Task.Delay(10); // Simulate async work
            executed = true;
        });

        await engine.Run("let x = 1;");

        Assert.True(executed, "Async task should have been executed");
    }

    [Fact(Timeout = 2000)]
    public async Task Run_CompletesWhenQueueIsEmpty()
    {
        var engine = new JsEngine();

        // Run with no scheduled tasks - should complete immediately
        var result = await engine.Run("5 + 5;");

        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ScheduleTask_CanBeCalledMultipleTimes()
    {
        var engine = new JsEngine();
        var count = 0;

        for (var i = 0; i < 10; i++)
            engine.ScheduleTask(() =>
            {
                count++;
                return Task.CompletedTask;
            });

        await engine.Run("let x = 1;");

        Assert.Equal(10, count);
    }

    [Fact(Timeout = 2000)]
    public async Task Run_AllowsInteractionWithHostFunctions()
    {
        var engine = new JsEngine();
        var capturedValues = new List<object?>();

        engine.SetGlobalFunction("capture", args =>
        {
            capturedValues.AddRange(args);
            return null;
        });

        engine.ScheduleTask(() =>
        {
            capturedValues.Add("from-task");
            return Task.CompletedTask;
        });

        await engine.Run("capture(1, 2, 3);");

        Assert.Contains(1d, capturedValues);
        Assert.Contains(2d, capturedValues);
        Assert.Contains(3d, capturedValues);
        Assert.Contains("from-task", capturedValues);
    }
}
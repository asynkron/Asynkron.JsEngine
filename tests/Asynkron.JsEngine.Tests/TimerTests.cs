using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class TimerTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task SetTimeout_ExecutesCallbackAfterDelay()
    {
        await using var engine = CreateEngine();
        var executed = false;

        engine.SetGlobalFunction("callback", _ =>
        {
            executed = true;
            return JsTypes.JsValue.Undefined;
        });

        await engine.Evaluate("""

                                     setTimeout(callback, 10);

                         """);

        Assert.True(executed, "setTimeout callback should have been executed");
    }

    [Fact(Timeout = 2000)]
    public async Task SetTimeout_ReturnsTimerId()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""

                                                  let timerId = setTimeout(function() {}, 100);
                                                  timerId;

                                      """);

        Assert.IsType<double>(result);
        Assert.True((double)result! >= 1);
    }

    [Fact(Timeout = 2000)]
    public async Task ClearTimeout_PreventsExecution()
    {
        await using var engine = CreateEngine();
        var executed = false;

        engine.SetGlobalFunction("callback", _ =>
        {
            executed = true;
            return JsTypes.JsValue.Undefined;
        });

        await engine.Evaluate("""

                                     let timerId = setTimeout(callback, 10);
                                     clearTimeout(timerId);

                         """);

        // Wait a bit to ensure callback would have executed if not cleared
        await Task.Delay(50);

        Assert.False(executed, "setTimeout callback should not have been executed after clearTimeout");
    }

    [Fact(Timeout = 2000)]
    public async Task SetInterval_ExecutesCallbackRepeatedly()
    {
        await using var engine = CreateEngine();
        var count = 0;

        engine.SetGlobalFunction("callback", _ =>
        {
            count++;
            return JsTypes.JsValue.Undefined;
        });

        engine.SetGlobalFunction("getCount", _ => new JsTypes.JsValue(count));

        var result = await engine.Evaluate("""

                                                  let timerId = setInterval(callback, 20);
                                                  setTimeout(function() {
                                                      clearInterval(timerId);
                                                  }, 100);
                                                  getCount();

                                      """);

        // Should have executed multiple times
        Assert.True(count >= 2, $"setInterval should have executed at least 2 times, but executed {count} times");
    }

    [Fact(Timeout = 2000)]
    public async Task ClearInterval_StopsExecution()
    {
        await using var engine = CreateEngine();
        var count = 0;

        engine.SetGlobalFunction("callback", _ =>
        {
            count++;
            return JsTypes.JsValue.Undefined;
        });

        await engine.Evaluate("""

                                     let timerId = setInterval(callback, 10);
                                     clearInterval(timerId);

                         """);

        // Wait a bit
        await Task.Delay(50);

        Assert.Equal(0, count);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task SetTimeout_WithZeroDelay_ExecutesAsynchronously()
    {
        await using var engine = CreateEngine();
        var order = new List<string>();

        engine.SetGlobalFunction("addToOrder", args =>
        {
            if (args.Count > 0 && args[0].ToObject() is string s)
            {
                order.Add(s);
            }

            return JsTypes.JsValue.Undefined;
        });

        await engine.Evaluate("""

                                     addToOrder("start");
                                     setTimeout(function() {
                                         addToOrder("timeout");
                                     }, 0);
                                     addToOrder("end");

                         """);

        Assert.Equal(new[] { "start", "end", "timeout" }, order);
    }

    [Fact(Timeout = 2000)]
    public async Task SetTimeout_CanAccessClosureVariables()
    {
        await using var engine = CreateEngine();
        var capturedValue = "";

        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0 && args[0].ToObject() is string s)
            {
                capturedValue = s;
            }

            return JsTypes.JsValue.Undefined;
        });

        await engine.Evaluate("""

                                     let message = "Hello from closure";
                                     setTimeout(function() {
                                         capture(message);
                                     }, 10);

                         """);

        Assert.Equal("Hello from closure", capturedValue);
    }
}

public class FastPathTimerTests(ITestOutputHelper output) : TimerTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceTimerTests(ITestOutputHelper output) : TimerTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}

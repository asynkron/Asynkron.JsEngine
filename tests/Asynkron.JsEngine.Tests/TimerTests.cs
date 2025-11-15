using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TimerTests
{
    [Fact(Timeout = 2000)]
    public async Task SetTimeout_ExecutesCallbackAfterDelay()
    {
        var engine = new JsEngine();
        var executed = false;

        engine.SetGlobalFunction("callback", args =>
        {
            executed = true;
            return null;
        });

        await engine.Run("""

                                     setTimeout(callback, 10);
                                 
                         """);

        Assert.True(executed, "setTimeout callback should have been executed");
    }

    [Fact(Timeout = 2000)]
    public async Task SetTimeout_ReturnsTimerId()
    {
        var engine = new JsEngine();

        var result = await engine.Run("""

                                                  let timerId = setTimeout(function() {}, 100);
                                                  timerId;
                                              
                                      """);

        Assert.IsType<double>(result);
        Assert.True((double)result! >= 1);
    }

    [Fact(Timeout = 2000)]
    public async Task ClearTimeout_PreventsExecution()
    {
        var engine = new JsEngine();
        var executed = false;

        engine.SetGlobalFunction("callback", args =>
        {
            executed = true;
            return null;
        });

        await engine.Run("""

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
        var engine = new JsEngine();
        var count = 0;

        engine.SetGlobalFunction("callback", args =>
        {
            count++;
            return null;
        });

        engine.SetGlobalFunction("getCount", args => count);

        var result = await engine.Run("""

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
        var engine = new JsEngine();
        var count = 0;

        engine.SetGlobalFunction("callback", args =>
        {
            count++;
            return null;
        });

        await engine.Run("""

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
        var engine = new JsEngine();
        var order = new List<string>();

        engine.SetGlobalFunction("addToOrder", args =>
        {
            if (args.Count > 0 && args[0] is string s)
            {
                order.Add(s);
            }

            return null;
        });

        await engine.Run("""

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
        var engine = new JsEngine();
        var capturedValue = "";

        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0 && args[0] is string s)
            {
                capturedValue = s;
            }

            return null;
        });

        await engine.Run("""

                                     let message = "Hello from closure";
                                     setTimeout(function() {
                                         capture(message);
                                     }, 10);
                                 
                         """);

        Assert.Equal("Hello from closure", capturedValue);
    }
}
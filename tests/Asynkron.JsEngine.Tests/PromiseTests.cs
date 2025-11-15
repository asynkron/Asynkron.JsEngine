using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class PromiseTests
{
    [Fact(Timeout = 2000)]
    public async Task Promise_CanBeResolved()
    {
        var engine = new JsEngine();
        var resolved = false;
        var resolvedValue = "";

        engine.SetGlobalFunction("checkResolved", args =>
        {
            resolved = true;
            if (args.Count > 0 && args[0] is string s) resolvedValue = s;
            return null;
        });

        await engine.Run("""

                                     let p = new Promise(function(resolve, reject) {
                                         resolve("test value");
                                     });
                                     
                                     p.then(function(value) {
                                         checkResolved(value);
                                     });
                                 
                         """);

        Assert.True(resolved);
        Assert.Equal("test value", resolvedValue);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Promise_CanBeRejected()
    {
        var engine = new JsEngine();
        var rejected = false;
        var rejectedReason = "";

        engine.SetGlobalFunction("checkRejected", args =>
        {
            rejected = true;
            if (args.Count > 0 && args[0] is string s) rejectedReason = s;
            return null;
        });

        await engine.Run("""

                                     let p = new Promise(function(resolve, reject) {
                                         reject("error reason");
                                     });
                                     
                                     p["catch"](function(reason) {
                                         checkRejected(reason);
                                     });
                                 
                         """);

        Assert.True(rejected);
        Assert.Equal("error reason", rejectedReason);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_ThenReturnsNewPromise()
    {
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0) result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p = new Promise(function(resolve, reject) {
                                         resolve(10);
                                     });
                                     
                                     p.then(function(value) {
                                         return value + 5;
                                     }).then(function(value) {
                                         captureResult(value);
                                     });
                                 
                         """);

        Assert.Equal("15", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_CanChainMultipleThen()
    {
        var engine = new JsEngine();
        var results = new List<string>();

        engine.SetGlobalFunction("addResult", args =>
        {
            if (args.Count > 0) results.Add(args[0]?.ToString() ?? "");
            return null;
        });

        await engine.Run("""

                                     let p = new Promise(function(resolve, reject) {
                                         resolve(1);
                                     });
                                     
                                     p.then(function(value) {
                                         addResult(value);
                                         return value + 1;
                                     }).then(function(value) {
                                         addResult(value);
                                         return value + 1;
                                     }).then(function(value) {
                                         addResult(value);
                                     });
                                 
                         """);

        Assert.Equal(new[] { "1", "2", "3" }, results);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_Resolve_CreatesResolvedPromise()
    {
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0) result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p = Promise.resolve("resolved value");
                                     p.then(function(value) {
                                         captureResult(value);
                                     });
                                 
                         """);

        Assert.Equal("resolved value", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_Reject_CreatesRejectedPromise()
    {
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0) result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p = Promise.reject("rejection reason");
                                     p["catch"](
                                     function(reason) {
                                         captureResult(reason);
                                     });
                                 
                         """);

        Assert.Equal("rejection reason", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_CatchHandlesRejection()
    {
        var engine = new JsEngine();
        var caught = false;

        engine.SetGlobalFunction("markCaught", args =>
        {
            caught = true;
            return null;
        });

        await engine.Run("""

                                     let p = new Promise(function(resolve, reject) {
                                         reject("error");
                                     });
                                     
                                     p.then(function(value) {
                                         // This should not execute
                                     })["catch"](function(reason) {
                                         markCaught();
                                     });
                                 
                         """);

        Assert.True(caught);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_Finally_ExecutesOnBothResolveAndReject()
    {
        var engine = new JsEngine();
        var finallyCount = 0;

        engine.SetGlobalFunction("incrementFinally", args =>
        {
            finallyCount++;
            return null;
        });

        await engine.Run("""

                                     let p1 = Promise.resolve("value");
                                     p1["finally"](function() {
                                         incrementFinally();
                                     });
                                     
                                     let p2 = Promise.reject("error");
                                     p2["finally"](function() {
                                         incrementFinally();
                                     });
                                 
                         """);

        Assert.Equal(2, finallyCount);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_All_ResolvesWhenAllResolve()
    {
        var engine = new JsEngine();
        var results = new List<string>();

        engine.SetGlobalFunction("addResult", args =>
        {
            if (args.Count > 0) results.Add(args[0]?.ToString() ?? "");
            return null;
        });

        await engine.Run("""

                                     let p1 = Promise.resolve(1);
                                     let p2 = Promise.resolve(2);
                                     let p3 = Promise.resolve(3);
                                     
                                     Promise.all([p1, p2, p3]).then(function(values) {
                                         addResult(values[0]);
                                         addResult(values[1]);
                                         addResult(values[2]);
                                     });
                                 
                         """);

        Assert.Equal(new[] { "1", "2", "3" }, results);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_All_RejectsWhenOneRejects()
    {
        var engine = new JsEngine();
        var rejected = false;
        var reason = "";

        engine.SetGlobalFunction("captureRejection", args =>
        {
            rejected = true;
            if (args.Count > 0) reason = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p1 = Promise.resolve(1);
                                     let p2 = Promise.reject("error");
                                     let p3 = Promise.resolve(3);
                                     
                                     Promise.all([p1, p2, p3])["catch"](
                                     function(err) {
                                         captureRejection(err);
                                     });
                                 
                         """);

        Assert.True(rejected);
        Assert.Equal("error", reason);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_Race_ResolvesWithFirstSettled()
    {
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0) result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p1 = Promise.resolve("first");
                                     let p2 = Promise.resolve("second");
                                     
                                     Promise.race([p1, p2]).then(function(value) {
                                         captureResult(value);
                                     });
                                 
                         """);

        Assert.Equal("first", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_IntegrationWithSetTimeout()
    {
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0) result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p = new Promise(function(resolve, reject) {
                                         setTimeout(function() {
                                             resolve("async value");
                                         }, 20);
                                     });
                                     
                                     p.then(function(value) {
                                         captureResult(value);
                                     });
                                 
                         """);

        Assert.Equal("async value", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_ExecutorRunsImmediately()
    {
        var engine = new JsEngine();
        var executorCount = 0;

        engine.SetGlobalFunction("markExecutorRan", args =>
        {
            executorCount++;
            return null;
        });

        await engine.Run("""

                                     markExecutorRan();
                                     let p = new Promise(function(resolve, reject) {
                                         markExecutorRan();
                                     });
                                 
                         """);

        Assert.Equal(2, executorCount);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_CatchReturnsResolvedPromise()
    {
        var engine = new JsEngine();
        var finalValue = "";

        engine.SetGlobalFunction("captureFinal", args =>
        {
            if (args.Count > 0) finalValue = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""

                                     let p = Promise.reject("error");
                                     
                                     let p2 = p["catch"](function(err) {
                                         return "recovered";
                                     });
                                     p2.then(function(value) {
                                         captureFinal(value);
                                     });
                                 
                         """);

        Assert.Equal("recovered", finalValue);
    }
}
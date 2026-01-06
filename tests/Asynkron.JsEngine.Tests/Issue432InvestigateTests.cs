using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Simple xunit logger for debugging.
/// </summary>
internal sealed class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly LogLevel _minLevel;

    public XunitLogger(ITestOutputHelper output, LogLevel minLevel = LogLevel.Debug)
    {
        _output = output;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (message.Contains("[DEBUG #432]"))
        {
            _output.WriteLine($"[{logLevel}] {message}");
        }
    }
}

/// <summary>
/// Investigation tests for #432 - Exception handling broken when block env combined with for await...of
/// </summary>
public class Issue432InvestigateTests
{
    private readonly ITestOutputHelper _output;

    public Issue432InvestigateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Layer 1: Minimal reproduction - simplest case that exhibits the behavior.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Layer1_MinimalRepro_TryCatchWithForAwaitOfThrows()
    {
        var logOutput = new List<string>();

        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = new XunitLogger(_output, LogLevel.Information)
        });

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            logOutput.Add(message);
            _output.WriteLine($"LOG: {message}");
            return JsValue.Null;
        });

        var errorCaught = false;
        engine.SetGlobalFunction("markError", _ =>
        {
            errorCaught = true;
            _output.WriteLine("LOG: Error caught!");
            return JsValue.Null;
        });

        // Simplified test case: for await...of with throw in next()
        await engine.Evaluate("""
            let syncIterable = {
                [Symbol.iterator]() {
                    let count = 0;
                    return {
                        next() {
                            count = count + 1;
                            log('next: ' + count);
                            if (count === 2) {
                                log('throwing');
                                throw 'test error';
                            }
                            return count <= 3
                                ? { value: count, done: false }
                                : { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('start');
                try {
                    log('entering try');
                    for await (let num of syncIterable) {
                        log('loop: ' + num);
                    }
                    log('loop done');
                } catch (e) {
                    log('caught: ' + e);
                    markError();
                }
                log('end');
            }

            test();
        """);

        _output.WriteLine("=== Log output ===");
        foreach (var msg in logOutput)
        {
            _output.WriteLine(msg);
        }

        _output.WriteLine($"Error caught: {errorCaught}");
        Assert.True(errorCaught, "Exception should have been caught by try/catch");
    }

    /// <summary>
    /// Layer 2: Same test but with regular for-of instead of for-await-of.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Layer2_RegularForOf_TryCatchWorks()
    {
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

        var errorCaught = false;
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return JsValue.Null;
        });

        engine.SetGlobalFunction("markError", _ =>
        {
            errorCaught = true;
            return JsValue.Null;
        });

        // Regular for-of (not async) - should work
        await engine.Evaluate("""
            let syncIterable = {
                [Symbol.iterator]() {
                    let count = 0;
                    return {
                        next() {
                            count = count + 1;
                            if (count === 2) {
                                throw 'test error';
                            }
                            return count <= 3
                                ? { value: count, done: false }
                                : { done: true };
                        }
                    };
                }
            };

            function test() {
                try {
                    for (let num of syncIterable) {
                        log('loop: ' + num);
                    }
                } catch (e) {
                    log('caught: ' + e);
                    markError();
                }
            }

            test();
        """);

        Assert.True(errorCaught, "Regular for-of exception should be caught");
    }

    /// <summary>
    /// Layer 3: Try/catch in async function with simple throw (no iterator).
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Layer3_AsyncFunction_SimpleTryCatch_Works()
    {
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

        var errorCaught = false;
        engine.SetGlobalFunction("markError", _ =>
        {
            errorCaught = true;
            return JsValue.Null;
        });

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            async function test() {
                log('start');
                try {
                    log('in try');
                    throw 'test error';
                } catch (e) {
                    log('caught: ' + e);
                    markError();
                }
                log('end');
            }

            test();
        """);

        Assert.True(errorCaught, "Simple try/catch in async function should work");
    }

    /// <summary>
    /// Layer 4: Try/catch in async function with for-await-of using var (no lexical binding).
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task Layer4_ForAwaitOf_WithVar_NotLet()
    {
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

        var errorCaught = false;
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return JsValue.Null;
        });

        engine.SetGlobalFunction("markError", _ =>
        {
            errorCaught = true;
            return JsValue.Null;
        });

        // Use var instead of let - this doesn't create a block scope for per-iteration bindings
        await engine.Evaluate("""
            let syncIterable = {
                [Symbol.iterator]() {
                    let count = 0;
                    return {
                        next() {
                            count = count + 1;
                            log('next: ' + count);
                            if (count === 2) {
                                log('throwing');
                                throw 'test error';
                            }
                            return count <= 3
                                ? { value: count, done: false }
                                : { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('start');
                try {
                    log('entering try');
                    for await (var num of syncIterable) {
                        log('loop: ' + num);
                    }
                    log('loop done');
                } catch (e) {
                    log('caught: ' + e);
                    markError();
                }
                log('end');
            }

            test();
        """);

        _output.WriteLine($"Error caught: {errorCaught}");
        Assert.True(errorCaught, "for await (var ...) exception should be caught");
    }
}

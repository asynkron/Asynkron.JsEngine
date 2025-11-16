using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Focused test to debug the global iterable issue with detailed error logging
/// </summary>
public class AsyncIterableDebugTest(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task GlobalIterable_CatchRejections()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOG] {msg}");
            return null;
        });

        await engine.Run(@"
            log('=== TEST START ===');

            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                log('Returning value=' + val);
                                return { value: val, done: false };
                            }
                            log('Returning done=true');
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('About to start for-await-of');
                log('typeof globalIterable: ' + typeof globalIterable);
                log('globalIterable is: ' + globalIterable);

                let result = '';
                for await (let item of globalIterable) {
                    log('In loop, item=' + item);
                    result = result + item;
                }

                log('After loop, result=' + result);
                return result;
            }

            // Call test() and explicitly catch rejections
            test().then(function(result) {
                log('Promise resolved with: ' + result);
            }).catch(function(error) {
                log('Promise rejected with: ' + error);
            });
        ");

        await Task.Delay(2000);
        output.WriteLine("=== TEST COMPLETE ===");
    }

    [Fact(Timeout = 5000)]
    public async Task GlobalIterable_ParsedCode()
    {
        await using var engine = new JsEngine();

        var code = @"
            let globalIterable = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            return { value: 'x', done: false };
                        }
                    };
                }
            };

            async function test() {
                let result = '';
                for await (let item of globalIterable) {
                    result = result + item;
                }
                return result;
            }
        ";

        var parsed = engine.Parse(code);
        output.WriteLine("=== PARSED S-EXPRESSION ===");
        output.WriteLine(parsed.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task GlobalIterable_WithLocalVariable()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOG] {msg}");
            return null;
        });

        await engine.Run(@"
            log('=== TEST WITH LOCAL COPY ===');

            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                log('Returning value=' + val);
                                return { value: val, done: false };
                            }
                            log('Returning done=true');
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('About to start - creating local copy');
                let localCopy = globalIterable;
                log('typeof localCopy: ' + typeof localCopy);

                let result = '';
                for await (let item of localCopy) {
                    log('In loop, item=' + item);
                    result = result + item;
                }

                log('After loop, result=' + result);
                return result;
            }

            // Call test() and explicitly catch rejections
            test().then(function(result) {
                log('Promise resolved with: ' + result);
            }).catch(function(error) {
                log('Promise rejected with: ' + error);
            });
        ");

        await Task.Delay(2000);
        output.WriteLine("=== TEST COMPLETE ===");
    }
}

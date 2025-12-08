using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test 1: using TDZ (temporal dead zone)
        Console.WriteLine("Test 1: for (using x of [x]) should throw ReferenceError (TDZ)");
        try
        {
            var result = await engine.Evaluate(@"
                var caught = false;
                var x = { [Symbol.dispose]() {} };
                try {
                    for (using x of [x]) {}
                } catch (e) {
                    caught = e instanceof ReferenceError;
                }
                caught;
            ");
            Console.WriteLine($"Result: {result}");
            Console.WriteLine(result?.ToString() == "True" ? "PASS" : "FAIL");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Test 2: iterator-as-proxy (step by step)
        Console.WriteLine("\nTest 2a: Basic proxy with Symbol.iterator");
        try
        {
            var result = await engine.Evaluate(@"
                var log = [];
                var obj = { [Symbol.iterator]: function() { log.push('iter'); return { next: function() { return { done: true }; } }; } };
                var proxy = new Proxy(obj, { get: function(t, k) { log.push('get ' + String(k)); return t[k]; } });
                for (var x of proxy) {}
                log.join(',');
            ");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("\nTest 2b: Iterator returns a proxy");
        try
        {
            var result = await engine.Evaluate(@"
                var log = [];
                var obj = {
                    [Symbol.iterator]: function() {
                        log.push('iter');
                        return new Proxy(
                            { next: function() { log.push('next'); return { done: true }; } },
                            { get: function(t, k) { log.push('iter-get ' + String(k)); return t[k]; } }
                        );
                    }
                };
                for (var x of obj) {}
                log.join(',');
            ");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("\nTest 2c: Iterator result proxy");
        try
        {
            var result = await engine.Evaluate(@"
                var log = [];
                var iterator = {
                    next: function() {
                        log.push('next called');
                        return new Proxy({ done: true, value: 1 }, {
                            get: function(t, k) { log.push('result-get ' + String(k)); return t[k]; }
                        });
                    }
                };
                var result = iterator.next();
                log.push('done=' + result.done);
                log.push('value=' + result.value);
                log.join(',');
            ");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("\nTest 2d: Full nested proxy");
        try
        {
            var result = await engine.Evaluate(@"
                var log = [];
                var iterable = new Proxy({
                    [Symbol.iterator]: function() {
                        log.push('Symbol.iterator');
                        return new Proxy({
                            next: function() {
                                log.push('next');
                                return new Proxy({
                                    done: true,
                                    value: undefined
                                }, {
                                    get: function(target, key) {
                                        log.push('get ' + String(key));
                                        return target[key];
                                    }
                                });
                            }
                        }, {
                            get: function(target, key) {
                                log.push('iterator get ' + String(key));
                                return target[key];
                            }
                        });
                    }
                }, {
                    get: function(target, key) {
                        log.push('iterable get ' + String(key));
                        return target[key];
                    }
                });

                for (var x of iterable) {}
                log.join(',');
            ");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Test 3: string-astral (for-of over astral Unicode)
        Console.WriteLine("\nTest 3: string-astral (for-of over astral Unicode)");
        try
        {
            var result = await engine.Evaluate(@"
                var chars = [];
                for (var c of '\uD83D\uDCA9') { // 💩 - U+1F4A9
                    chars.push(c);
                }
                chars.length === 1 && chars[0] === '\uD83D\uDCA9';
            ");
            Console.WriteLine($"Result: {result}");
            Console.WriteLine(result?.ToString() == "True" ? "PASS" : "FAIL");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Test 4b: throw() method on iterator
        Console.WriteLine("\nTest 4b: throw() method on iterator");
        try
        {
            var result = await engine.Evaluate(@"
                function makeIterator() {
                    return {
                        [Symbol.iterator]() {
                            return {
                                next() {
                                    return { value: 'initial', done: false };
                                },
                                return() {
                                    return { value: 'done', done: true };
                                },
                                throw(err) {
                                    return { value: 'handled:' + err, done: false };
                                }
                            };
                        }
                    };
                }

                function* outer() {
                    yield* makeIterator();
                    yield 'after';
                }

                let g = outer();
                let first = g.next().value;
                let second = g.throw('boom').value;
                first + ',' + second;
            ");
            Console.WriteLine($"Result: {result}");
            // Expected: initial,handled:boom
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Test 4: iterator-close-throw-get-method-abrupt
        Console.WriteLine("\nTest 4: iterator-close-throw-get-method-abrupt");
        try
        {
            var result = await engine.Evaluate(@"
                var returnCalled = false;
                var getReturnError = new Error('get return');
                var bodyError = new Error('body');
                var caughtError = null;

                var iterable = {
                    [Symbol.iterator]: function() {
                        return {
                            next: function() {
                                return { value: 1, done: false };
                            },
                            get return() {
                                throw getReturnError;
                            }
                        };
                    }
                };

                try {
                    for (var x of iterable) {
                        throw bodyError;
                    }
                } catch (e) {
                    caughtError = e;
                }

                // Per spec 7.4.6: when iterator.return getter throws during abrupt completion,
                // the original completion is preserved (bodyError should be re-thrown)
                caughtError === bodyError ? 'original-preserved' : 'wrong-error';
            ");
            Console.WriteLine($"Result: {result}");
            Console.WriteLine(result?.ToString() == "original-preserved" ? "PASS" : "FAIL");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

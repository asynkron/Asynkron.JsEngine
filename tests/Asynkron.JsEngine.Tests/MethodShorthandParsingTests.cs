using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class MethodShorthandParsingTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task TestJ_ParseMethodShorthandVsRegularFunction()
    {
        output.WriteLine("=== Test J: Parse and Compare S-Expressions ===");
        
        var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('=== Comparing method shorthand vs regular function ===');
            
            // Test 1: Regular function syntax
            let obj1 = {
                [Symbol.iterator]() {
                    return {
                        next: function() {
                            return { value: 'x', done: false };
                        }
                    };
                }
            };
            
            // Test 2: Method shorthand syntax
            let obj2 = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            return { value: 'y', done: false };
                        }
                    };
                }
            };
            
            log('Getting iterators...');
            let iter1 = obj1[Symbol.iterator]();
            let iter2 = obj2[Symbol.iterator]();
            
            log('iter1.next type: ' + typeof iter1.next);
            log('iter2.next type: ' + typeof iter2.next);
            
            log('Calling iter1.next()...');
            try {
                let result1 = iter1.next();
                log('iter1.next() returned: ' + JSON.stringify(result1));
            } catch (e) {
                log('iter1.next() ERROR: ' + e);
            }
            
            log('Calling iter2.next()...');
            try {
                let result2 = iter2.next();
                log('iter2.next() returned: ' + JSON.stringify(result2));
            } catch (e) {
                log('iter2.next() ERROR: ' + e);
            }
        ");

        await Task.Delay(500);

        // Check exceptions
        var exceptions = new List<ExceptionInfo>();
        while (engine.Exceptions().TryRead(out var ex))
        {
            exceptions.Add(ex);
        }

        output.WriteLine("");
        output.WriteLine($"=== EXCEPTIONS: {exceptions.Count} ===");
        foreach (var ex in exceptions)
        {
            output.WriteLine($"  - {ex.Message} (Context: {ex.Context})");
        }

        output.WriteLine("");
        output.WriteLine("This directly tests if method shorthand fails when returned from Symbol.iterator");
    }

    [Fact(Timeout = 5000)]
    public async Task TestK_MethodShorthandInDifferentContexts()
    {
        output.WriteLine("=== Test K: Method Shorthand in Different Contexts ===");
        
        var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('=== Test 1: Method shorthand in simple object ===');
            let simple = {
                method() {
                    return 'simple works';
                }
            };
            log('Result: ' + simple.method());
            
            log('');
            log('=== Test 2: Method shorthand in object returned from function ===');
            function getObject() {
                return {
                    method() {
                        return 'returned works';
                    }
                };
            }
            let returned = getObject();
            log('Result: ' + returned.method());
            
            log('');
            log('=== Test 3: Method shorthand in object returned from computed property ===');
            let obj3 = {
                [Symbol.iterator]() {
                    return {
                        method() {
                            return 'computed works';
                        }
                    };
                }
            };
            let fromComputed = obj3[Symbol.iterator]();
            log('Result: ' + fromComputed.method());
            
            log('');
            log('=== Test 4: Method shorthand as next() in iterator ===');
            let obj4 = {
                [Symbol.iterator]() {
                    let count = 0;
                    return {
                        next() {
                            if (count < 2) {
                                return { value: count++, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };
            let iter4 = obj4[Symbol.iterator]();
            log('Calling next() first time...');
            try {
                let r1 = iter4.next();
                log('Result 1: ' + JSON.stringify(r1));
                let r2 = iter4.next();
                log('Result 2: ' + JSON.stringify(r2));
                let r3 = iter4.next();
                log('Result 3: ' + JSON.stringify(r3));
            } catch (e) {
                log('ERROR: ' + e);
            }
        ");

        await Task.Delay(500);

        // Check exceptions
        var exceptions = new List<ExceptionInfo>();
        while (engine.Exceptions().TryRead(out var ex))
        {
            exceptions.Add(ex);
        }

        output.WriteLine("");
        output.WriteLine($"=== EXCEPTIONS: {exceptions.Count} ===");
        foreach (var ex in exceptions)
        {
            output.WriteLine($"  - {ex.Message} (Context: {ex.Context})");
        }

        output.WriteLine("");
        output.WriteLine("This tests method shorthand in progressively complex scenarios");
    }

}

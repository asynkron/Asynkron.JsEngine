using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var code = """
var log = [];
function source() {
    log.push('source');
    var iterator = {
        next: function() {
            log.push('iterator-step');
            return {
                get done() {
                    log.push('iterator-done');
                    return true;
                },
                get value() {
                    log.push('iterator-value');
                    return 1;
                }
            };
        }
    };
    var source = {};
    source[Symbol.iterator] = function() {
        log.push('iterator');
        return iterator;
    };
    return source;
}
function target() {
    log.push('target');
    return target = {
        set q(v) {
            log.push('set');
        }
    };
}
function targetKey() {
    log.push('target-key');
    return {
        toString: function() {
            log.push('target-key-tostring');
            return 'q';
        }
    };
}

([target()[targetKey()]] = source());

console.log('Actual:', JSON.stringify(log));
console.log('Expected:', JSON.stringify([
    'source', 'iterator',
    'target', 'target-key',
    'iterator-step', 'iterator-done',
    'target-key-tostring', 'set',
]));
""";

        await engine.Evaluate(code);

        // Get the log array and print it
        var log = await engine.Evaluate("JSON.stringify(log)");
        Console.WriteLine($"Actual: {log}");
        Console.WriteLine("Expected: [\"source\",\"iterator\",\"target\",\"target-key\",\"iterator-step\",\"iterator-done\",\"target-key-tostring\",\"set\"]");
    }
}

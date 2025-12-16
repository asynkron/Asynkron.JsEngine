using Asynkron.JsEngine;
using System;

var engine = new JsEngine();

var test = @"
var result = 'not_set';

async function f(x = y, y) {
  return 'should not reach here';
}

f().then(
  v => { result = 'resolved: ' + v; },
  e => { result = 'rejected: ' + e.constructor.name; }
);

// Return a promise that waits for the rejection handler
new Promise(resolve => {
  setTimeout(() => resolve(result), 10);
});
";

Console.WriteLine("Test: Async function TDZ in default params");
try {
    var result = await engine.Evaluate(test).ConfigureAwait(false);
    Console.WriteLine($"  Result: {result}");
    Console.WriteLine("  Expected: rejected: ReferenceError");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}

using Asynkron.JsEngine;
using System;

var engine = new JsEngine();

// Test: with statement base object
var test = @"
var obj = {
  attr: 'attribute',
  fn: function() {
    return this;
  }
};

var viaCall;
var viaMember;

with (obj) {
  // When called through 'with', 'this' should be the with-object
  viaCall = fn();
  viaMember = obj.fn();
}

(viaCall === obj) + ',' + (viaMember === obj);
";

Console.WriteLine("Test: with statement base object");
try {
    var result = await engine.Evaluate(test).ConfigureAwait(false);
    Console.WriteLine($"  Result: {result}");
    Console.WriteLine("  Expected: true,true");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}

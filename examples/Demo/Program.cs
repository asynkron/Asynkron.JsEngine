using Asynkron.JsEngine;
using System;

var engine = new JsEngine();

// Test: static async method called via reference
var test = @"
// Test 1: regular async function via reference
async function asyncFn(val = 1) { return val; }
var ref1 = asyncFn;
var r1 = ref1(42);
var t1 = typeof r1;

// Test 2: static async method via direct call
var C = class {
  static async method(val = 1) { return val; }
};
var r2 = C.method(42);
var t2 = typeof r2;

// Test 3: static async method via reference
var ref3 = C.method;
var r3 = ref3(42);
var t3 = typeof r3;

// Test 4: check if ref3 is same as C.method
var same = ref3 === C.method;

'regular async via ref: ' + t1 + ', static direct: ' + t2 + ', static via ref: ' + t3 + ', same fn: ' + same;
";

Console.WriteLine("Test: static async method via reference");
try {
    var result = await engine.Evaluate(test).ConfigureAwait(false);
    Console.WriteLine($"  Result: {result}");
    Console.WriteLine("  Expected: resolved: false");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}

using Asynkron.JsEngine;
using Asynkron.JsEngine.Execution;

await using var engine = new JsEngine(new JsEngineOptions { AllowScriptSlotAnalysis = true });

ExecutionPlanDiagnostics.Reset();
var result = engine.EvaluateSync(@"
    'use strict';
    var o = {a: 1, b: 2};
    var keys = [];
    for (var key in o) { keys.push(key); }
    keys.join(',');
");
var (a, s, f) = ExecutionPlanDiagnostics.Snapshot();
Console.WriteLine($"Strict for-in: {result} (IR: {a}/{s}/{f})");

// Non-strict
ExecutionPlanDiagnostics.Reset();
var result2 = engine.EvaluateSync(@"
    var o2 = {c: 3, d: 4};
    var keys2 = [];
    for (var key2 in o2) { keys2.push(key2); }
    keys2.join(',');
");
var (a2, s2, f2) = ExecutionPlanDiagnostics.Snapshot();
Console.WriteLine($"Non-strict for-in: {result2} (IR: {a2}/{s2}/{f2})");

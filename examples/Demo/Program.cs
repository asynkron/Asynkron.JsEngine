using Asynkron.JsEngine;

var engine = new JsEngine();
engine.SetGlobalFunction("log", args =>
{
    Console.WriteLine(args.Count > 0 ? args[0]?.ToString() : string.Empty);
    return null;
});

var script = @"
// Test Array constructor with size parameter
var arr = new Array(5);
log('After new Array(5):');
log('typeof arr: ' + typeof arr);
log('arr.length: ' + arr.length);
log('arr[0]: ' + arr[0]);
log('arr[1]: ' + arr[1]);

// Test manual array creation
var arr2 = [null, null, null, null, null];
log('');
log('After [null, null, null, null, null]:');
log('arr2.length: ' + arr2.length);
log('arr2[0]: ' + arr2[0]);
";

await engine.Evaluate(script);
Console.WriteLine("Test completed");

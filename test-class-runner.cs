using Asynkron.JsEngine;
using System.IO;

var engine = new JsEngine();
var code = File.ReadAllText("/Users/rogerjohansson/git/asynkron/Asynkron.JsEngine/test-class.js");

try {
    engine.Execute(code);
    Console.WriteLine("✓ Execution completed successfully");
} catch (Exception ex) {
    Console.WriteLine($"✗ Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

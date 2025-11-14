using Asynkron.JsEngine;

var code = """
var n = { x: 1, y: 2 };
var a = {};

Object.keys(n).forEach(function (k) {
    var d = Object.getOwnPropertyDescriptor(n, k);
    Object.defineProperty(a, k, d.get ? d : {
        enumerable: true,
        get: function () {
            return n[k];
        }
    });
});

a.x;
""";

var engine = new JsEngine();
try {
    var result = await engine.Evaluate(code);
    Console.WriteLine($"Result: {result}");
} catch (Exception ex) {
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Type: {ex.GetType().Name}");
}

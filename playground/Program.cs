using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine;

internal class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var scenarios = new[]
        {
            ("simple var", "var f = 123; f;"),
            ("block func after", "var f = 123; var before = f; { function f() {} } before;"),
            ("switch func after", "var f = 123; var before = f; switch (1) { case 1: function f() {} } before;"),
            ("if func after", "var f = 123; var before = f; if (true) function f() {} before;"),
            ("switch case assert", "var f = 123; if (f !== 123) throw 'bad'; switch (1) { case 1: function f() {} } 'done';"),
            ("class accessor descriptor", @"
                class MyClass {
                    constructor() { this._value = 0; }
                    get value() { return this._value; }
                    set value(v) { this._value = v; }
                }
                var obj = new MyClass();
                obj.value = 100;
                var descriptor = Object.getOwnPropertyDescriptor(MyClass.prototype, 'value');
                JSON.stringify({
                    getType: typeof descriptor.get,
                    setType: typeof descriptor.set,
                    descriptorHasGet: descriptor.hasOwnProperty('get'),
                    descriptorHasSet: descriptor.hasOwnProperty('set'),
                    value: obj.value
                });
            ")
        };

        foreach (var (label, code) in scenarios)
        {
            var result = await engine.Evaluate(code);
            Console.WriteLine($"{label}: {result ?? "null"} ({result?.GetType()})");
        }
    }
}

using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine;

internal class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var source = @"function f2() { return
1; } var result = f2(); result;";
        var lexer = new Asynkron.JsEngine.Parser.Lexer(source);
        var tokens = lexer.Tokenize();
        Console.WriteLine("Tokens:");
        foreach (var token in tokens)
        {
            var escaped = token.Lexeme.Replace("\n", "\\n");
            Console.WriteLine($"{token.Type,-12} line={token.Line} col={token.Column} lexeme='{escaped}'");
        }

        var scenarios = new[]
        {
            ("simple var", "var f = 123; f;"),
            ("block func after", "var f = 123; var before = f; { function f() {} } before;"),
            ("switch func after", "var f = 123; var before = f; switch (1) { case 1: function f() {} } before;"),
            ("if func after", "var f = 123; var before = f; if (true) function f() {} before;"),
            ("switch case assert", "var f = 123; if (f !== 123) throw 'bad'; switch (1) { case 1: function f() {} } 'done';"),
            ("return newline", @"function f(){ return
1; } f();"),
            ("literal undefined", "undefined;"),
            ("return newline check", @"function f(){ return
1; }
var value = f();
value === undefined ? 'undefined' : (typeof value + ':' + value);"),
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
            "),
            ("private static brand", @"
                class C1 {
                  static set #m(v) { this._v = v; }
                  static access() { this.#m = 'test262'; }
                }

                class C2 {
                  static set #m(v) { this._v = v; }
                  static access() { this.#m = 'test262'; }
                }

                C1.access();
                C2.access();
                try {
                    C1.access.call(C2);
                    'no throw';
                } catch (err) {
                    'threw: ' + err;
                }
                + ' | C1 keys: ' + Object.getOwnPropertyNames(C1).join(',') + ' | C2 keys: ' + Object.getOwnPropertyNames(C2).join(',')
            "),
            ("class scope open no heritage", @"
                try {
                    var probeBefore = function() { return C; };
                    var C = 'outside';

                    var cls = class C {
                      probe() {
                        return C;
                      }
                      modify() {
                        C = null;
                      }
                    };

                    return JSON.stringify({
                        typeofCls: typeof cls,
                        probeBefore: probeBefore(),
                        probeResultType: typeof cls.prototype.probe,
                        probeInvoke: cls.prototype.probe(),
                        modifyThrows: (function(){
                            try { cls.prototype.modify(); return 'no throw'; }
                            catch (err) { return err?.name || err; }
                        })(),
                        probeAfterModify: cls.prototype.probe()
                    });
                } catch (err) {
                    return 'threw: ' + err + ' stack: ' + err.stack;
                }
            "),
            ("class scope open heritage raw", @"
                try {
                    'use strict';
                    var probeBefore = function() { return C; };
                    var C = 'outside';

                    var cls = class C {
                      probe() { return C; }
                      modify() { C = null; }
                    };

                    var result1 = cls.prototype.probe();
                    var afterThrows;
                    try {
                        cls.prototype.modify();
                        afterThrows = 'no throw';
                    } catch (err) {
                        afterThrows = err?.name || err;
                    }
                    return JSON.stringify({
                        typeofCls: typeof cls,
                        resultType: typeof result1,
                        equalsCls: result1 === cls,
                        afterThrows,
                        afterProbe: typeof cls.prototype.probe()
                    });
                } catch (err) {
                    return 'threw: ' + err + ' stack: ' + err.stack;
                }
            ")
        };

        foreach (var (label, code) in scenarios)
        {
            var result = await engine.Evaluate(code);
            Console.WriteLine($"{label}: {result ?? "null"} ({result?.GetType()})");
        }
    }
}

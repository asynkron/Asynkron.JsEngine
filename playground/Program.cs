using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var typeProtoEvery = await engine.Evaluate("typeof Array.prototype.every;");
        var typeInstanceEvery = await engine.Evaluate("typeof (new Array()).every;");
        Console.WriteLine($"Array.prototype.every: {typeProtoEvery}");
        Console.WriteLine($"(new Array()).every: {typeInstanceEvery}");

        var protoCtorName = await engine.Evaluate("Array.prototype.constructor && Array.prototype.constructor.name;");
        Console.WriteLine($"Array.prototype.constructor name: {protoCtorName}");

        var protoProto = await engine.Evaluate("Object.getPrototypeOf(Array.prototype) === Object.prototype;");
        Console.WriteLine($"Array.prototype[[Prototype]] is Object.prototype: {protoProto}");

        var subclassInfo = await engine.Evaluate("""
            function foo() {}
            foo.prototype = new Array(1, 2, 3);
            var f = new foo();
            f.length = 2;
            ({
                typeEvery: typeof f.every,
                protoIsArrayProto: Object.getPrototypeOf(foo.prototype) === Array.prototype,
                prototypeTypeEvery: typeof foo.prototype.every,
                arrayProtoEvery: typeof Array.prototype.every
            });
            """);

        if (subclassInfo is System.Collections.IDictionary dict)
        {
            Console.WriteLine($"typeof f.every: {dict["typeEvery"]}");
            Console.WriteLine($"foo.prototype -> Array.prototype: {dict["protoIsArrayProto"]}");
            Console.WriteLine($"typeof foo.prototype.every: {dict["prototypeTypeEvery"]}");
            Console.WriteLine($"typeof Array.prototype.every: {dict["arrayProtoEvery"]}");
        }

        var arrTag = await engine.Evaluate("Object.prototype.toString.call(new Array());");
        Console.WriteLine($"Object.prototype.toString.call(new Array()): {arrTag}");
    }
}

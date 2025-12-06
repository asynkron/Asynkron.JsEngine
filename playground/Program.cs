using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            (function () {
              var C = class {
                #x = 1;
                x() { return this.#x; }
              };
              var c = new C();
              var p = new Proxy(c, {});
              return {
                protoMatch: Object.getPrototypeOf(c) === C.prototype,
                protoEqualsDesc: Object.getPrototypeOf(c) === Object.getOwnPropertyDescriptor(C, "prototype").value,
                protoIsNull: Object.getPrototypeOf(c) === null,
                protoIsObjectProto: Object.getPrototypeOf(c) === Object.prototype,
                protoActualCtor: Object.getPrototypeOf(c)?.constructor?.name,
                protoActualKeys: Object.getOwnPropertyNames(Object.getPrototypeOf(c) || {}),
                cType: typeof c.x,
                pType: typeof p.x,
                ctorProto: C.prototype,
                actualProto: Object.getPrototypeOf(c),
                instance: c,
                ctor: C
              };
            })();
            """);

        if (result is JsObject obj)
        {
            foreach (var kvp in obj)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }

            if (obj["ctorProto"] is JsObject ctorProto && obj["actualProto"] is JsObject actualProto)
            {
                Console.WriteLine($"ctorProto.Origin: {ctorProto.Origin}");
                Console.WriteLine($"actualProto.Origin: {actualProto.Origin}");
                Console.WriteLine($"ReferenceEquals: {ReferenceEquals(ctorProto, actualProto)}");
                Console.WriteLine($"Instance.PrototypeAccessor: {((JsObject)obj["instance"]).PrototypeAccessor?.GetType().Name}");
                Console.WriteLine($"Instance.Prototype (CLR): {((JsObject)obj["instance"]).Prototype?.GetType().Name}");
            }

            if (obj["ctor"] is object ctorObj)
            {
                var isDerivedProp = ctorObj.GetType().GetProperty("IsDerivedClassConstructor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var isClassCtorProp = ctorObj.GetType().GetProperty("IsClassConstructor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Console.WriteLine($"ctor.IsClassConstructor: {isClassCtorProp?.GetValue(ctorObj) ?? "null"}");
                Console.WriteLine($"ctor.IsDerivedClassConstructor: {isDerivedProp?.GetValue(ctorObj) ?? "null"}");

                if (ctorObj is IJsPropertyAccessor ctorAccessor &&
                    ctorAccessor.TryGetProperty("prototype", out var protoVal) &&
                    protoVal is JsObject protoObj)
                {
                    Console.WriteLine($"ctor.prototype CLR Origin: {protoObj.Origin}");
                    Console.WriteLine($"ctor.prototype keys: {string.Join(",", protoObj.Keys)}");
                }
            }
        }
        else
        {
            Console.WriteLine(result ?? "null");
        }
    }
}

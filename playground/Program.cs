using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.StdLib;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            (function() {
              var bool = new Boolean(true);
              var arr1 = [].concat(bool);
              bool[Symbol.isConcatSpreadable] = true;
              bool.length = 3;
              bool[0] = 1; bool[1] = 2; bool[2] = 3;
              var arr2 = [].concat(bool);
              Boolean.prototype[Symbol.isConcatSpreadable] = true;
              Boolean.prototype.length = 3;
              Boolean.prototype[0] = 1;
              Boolean.prototype[1] = 2;
              Boolean.prototype[2] = 3;
              var arr3 = [].concat(new Boolean(true));
              var str = new String("yuck💩");
              var str1 = [].concat(str);
              str[Symbol.isConcatSpreadable] = true;
              var str2 = [].concat(str);
              String.prototype[Symbol.isConcatSpreadable] = true;
              var str3 = [].concat(new String("yuck💩"));
              return {
                bool,
                str,
                arr1Len: arr1.length,
                arr1Type0: typeof arr1[0],
                arr1IsBoolObj: arr1[0] instanceof Boolean,
                arr1Tag0: Object.prototype.toString.call(arr1[0]),
                arr2Len: arr2.length,
                arr2Type0: typeof arr2[0],
                arr2Val0: arr2[0],
                arr2Type1: typeof arr2[1],
                arr2Val1: arr2[1],
                arr2Type2: typeof arr2[2],
                arr2Val2: arr2[2],
                arr2IsBoolObj0: arr2[0] instanceof Boolean,
                arr2IsNumber0: arr2[0] instanceof Number,
                arr2Tag0: Object.prototype.toString.call(arr2[0]),
                arr3Len: arr3.length,
                arr3Vals: [arr3[0], arr3[1], arr3[2]],
                arr3Types: [typeof arr3[0], typeof arr3[1], typeof arr3[2]],
                arr3Tags: [Object.prototype.toString.call(arr3[0]), Object.prototype.toString.call(arr3[1]), Object.prototype.toString.call(arr3[2])],
                str1Len: str1.length,
                str1Type0: typeof str1[0],
                str2Len: str2.length,
                str2Vals: [str2[0], str2[1], str2[2], str2[3], str2[4], str2[5]],
                str2Types: [typeof str2[0], typeof str2[1], typeof str2[2], typeof str2[3], typeof str2[4], typeof str2[5]],
                str3Len: str3.length,
                str3Vals: [str3[0], str3[1], str3[2], str3[3], str3[4], str3[5]],
                str3Types: [typeof str3[0], typeof str3[1], typeof str3[2], typeof str3[3], typeof str3[4], typeof str3[5]]
              };
            })();
            """);

        if (result is System.Collections.IDictionary dict)
        {
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                Console.WriteLine($"{entry.Key}: {entry.Value}");
            }

            var boolObj = dict["bool"];
            var strObj = dict["str"];
            var realmStateProp = typeof(JsEngine).GetProperty("RealmState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var realmState = realmStateProp?.GetValue(engine);
            var isConcatSpreadable = typeof(StandardLibrary).GetMethod("IsConcatSpreadable",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var parameters = new object?[] { boolObj, realmState, "dbg", null };
            var spreadBool = (bool)(isConcatSpreadable?.Invoke(null, parameters) ?? false);
            Console.WriteLine($"IsConcatSpreadable(boolObj): {spreadBool}, accessor null: {parameters[3] is null}");
            parameters = new object?[] { strObj, realmState, "dbg", null };
            var spreadStr = (bool)(isConcatSpreadable?.Invoke(null, parameters) ?? false);
            Console.WriteLine($"IsConcatSpreadable(strObj): {spreadStr}, accessor null: {parameters[3] is null}");
        }
    }
}

using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate("""
                // S8.4_A9_T2 test
                var str="";
                var strObj=new String("");
                var strObj_=new String();

                // CHECK#1
                if (str.constructor !== strObj.constructor){
                 throw new Error('#1: "".constructor === new String("").constructor');
                }

                // CHECK#2
                if (str.constructor !== strObj_.constructor){
                 throw new Error('#2: "".constructor === new String().constructor');
                }

                // CHECK#3
                if (str != strObj){
                 throw new Error('#3: values of str=""; and strObj=new String(""); are equal');
                }

                // CHECK#4
                if (str === strObj){
                 throw new Error('#4: objects of str=""; and strObj=new String(""); are different');
                }

                // CHECK#5
                if (str != strObj_){
                 throw new Error('#5: values of str=""; and strObj=new String(); are equal');
                }

                // CHECK#6
                if (str === strObj_){
                 throw new Error('#6: objects of str=""; and strObj=new String(); are different');
                }

                console.log("All S8.4_A9_T2 tests PASSED!");
                """);

            Console.WriteLine("Test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
        }
    }
}

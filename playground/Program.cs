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
            var result = await engine.Evaluate(@"
                function* g5() { (yield) ? yield : yield; }

                var iter = g5();
                var result1 = iter.next();
                console.log('First next:', JSON.stringify(result1));

                var result2 = iter.next();
                console.log('Second next:', JSON.stringify(result2));

                var result3 = iter.next();
                console.log('Third next:', JSON.stringify(result3));
                ");

            Console.WriteLine($"Test completed! Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}

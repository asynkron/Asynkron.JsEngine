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
                // Test all the cases from the Test262 test
                function* g1() { (yield) }
                function* g2() { [yield] }
                function* g3() { {yield} }
                function* g4() { yield, yield; }
                function* g5() { (yield) ? yield : yield; }

                // Test g1
                var iter = g1();
                var r1 = iter.next();
                console.log('g1 first:', JSON.stringify(r1));
                var r2 = iter.next();
                console.log('g1 second:', JSON.stringify(r2));

                // Test g5 (the problematic one)
                iter = g5();
                r1 = iter.next();
                console.log('g5 first:', JSON.stringify(r1));
                r2 = iter.next();
                console.log('g5 second:', JSON.stringify(r2));
                var r3 = iter.next();
                console.log('g5 third:', JSON.stringify(r3));

                // Verify the fix
                if (r3.done === true && r3.value === undefined) {
                    console.log('TEST PASSED: g5 completes correctly after two yields');
                    'PASS';
                } else {
                    console.log('TEST FAILED: g5 should be done after third next()');
                    throw new Error('Expected done=true after third next()');
                }
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

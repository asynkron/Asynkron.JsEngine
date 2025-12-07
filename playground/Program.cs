using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test 1: Basic super() call (should work)
        Console.WriteLine("Test 1: Basic super() call");
        try
        {
            var result = await engine.Evaluate(@"
                class C1 extends class {} {
                    constructor() {
                        super();
                    }
                }
                var o = new C1();
                typeof o;
            ");
            Console.WriteLine($"Result: {result} (expected: object)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Test 2: Arrow function calling super, then access this
        Console.WriteLine("\nTest 2: Arrow function calling super, then access this");
        try
        {
            var result = await engine.Evaluate(@"
                class C2 extends class {} {
                    constructor() {
                        var f = () => super();
                        f();
                        return this;  // Explicitly return this
                    }
                }
                var o = new C2();
                typeof o;
            ");
            Console.WriteLine($"Result: {result} (expected: object)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Test 3: Arrow function calling super, implicit return
        Console.WriteLine("\nTest 3: Arrow function calling super, implicit return");
        try
        {
            var result = await engine.Evaluate(@"
                class C3 extends class {} {
                    constructor() {
                        var f = () => super();
                        f();
                        // No return - constructor should return this implicitly
                    }
                }
                var o = new C3();
                typeof o;
            ");
            Console.WriteLine($"Result: {result} (expected: object)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test: Import defer [[Set]] should not trigger evaluation
        Console.WriteLine("Test: Import defer [[Set]] should not trigger evaluation");

        // Setup module loader for fixtures
        engine.SetModuleLoader((specifier, _) =>
        {
            Console.WriteLine($"  Loading module: {specifier}");
            var normalized = specifier.TrimStart('.', '/');
            return normalized switch
            {
                "setup_FIXTURE.js" => "globalThis.evaluations = [];",
                "dep_FIXTURE.js" => "globalThis.evaluations.push('dep'); export var exported = 42;",
                _ => throw new Exception($"Unknown module: {specifier} (normalized: {normalized})")
            };
        });

        try
        {
            var result = await engine.EvaluateModule(@"
                import './setup_FIXTURE.js';
                import defer * as ns from './dep_FIXTURE.js';

                const pre = globalThis.evaluations.length;

                try {
                    ns.exported = 'hi';
                } catch (e) {
                    // Expected - module namespace is immutable
                }

                const post = globalThis.evaluations.length;

                `pre=${pre}, post=${post}, passed=${pre === 0 && post === 0}`;
            ", "test.mjs");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}

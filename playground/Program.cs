using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var source = "var public = 1;";
            Console.WriteLine("Tokens:");
            var lexer = new Asynkron.JsEngine.Parser.JsLexer(source);
            var tokens = lexer.Tokenize();
            foreach (var token in tokens)
            {
                Console.WriteLine($"{token.Type}: '{token.Lexeme}'");
            }

            var result = await engine.Evaluate($"""
                'use strict';
                eval("{source}");
                """);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Eval threw: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

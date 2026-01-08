using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Diagnostic tests to understand what's failing in SunSpider tests
/// </summary>
public class SunSpiderDiagnosticTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000, Skip = "Too Slow")]
    public async Task SimpleThrow_WithStringConcatenation()
    {
        await using var engine = new JsEngine();

        try
        {
            await engine.Evaluate(@"
                var expected = 'hello';
                var actual = 'world';
                throw 'ERROR: expected ' + expected + ' but got ' + actual;
            ");
            output.WriteLine("No exception thrown - UNEXPECTED");
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine($"ThrownValue: '{ex.ThrownValue}'");
            output.WriteLine($"ThrownValue type: {ex.ThrownValue.ObjectValue?.GetType()}");
            output.WriteLine($"ThrownValue == null: {ex.ThrownValue.ObjectValue == null}");
            output.WriteLine($"Message: {ex.Message}");

            // This should have a proper error message
            Assert.False(ex.ThrownValue.IsUndefined);
            Assert.Contains("ERROR", ex.ThrownValue.ToString());
        }
    }

    [Fact(Timeout = 10000, Skip = "Too Slow")]
    public async Task CryptoMd5_Diagnose()
    {
        await using var engine = new JsEngine();

        var content = GetEmbeddedFile("crypto-md5.js");
        output.WriteLine($"Script length: {content.Length}");

        try
        {
            await engine.Evaluate(content);
            output.WriteLine("Script executed successfully!");
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine($"\nThrowSignal caught!");
            output.WriteLine($"Message: {ex.Message}");
            output.WriteLine($"ThrownValue: {ex.ThrownValue}");
            output.WriteLine($"ThrownValue type: {ex.ThrownValue.ObjectValue?.GetType()}");
            output.WriteLine($"ThrownValue == null: {ex.ThrownValue.ObjectValue == null}");

            // Check debug messages
            var debugMessages = new List<DebugMessage>();
            while (engine.DebugMessages().TryRead(out var msg))
            {
                debugMessages.Add(msg);
                output.WriteLine($"\nDebug message {debugMessages.Count}:");
                output.WriteLine($"  Variables count: {msg.Variables.Count}");
                foreach (var (key, value) in msg.Variables)
                {
                    var valueStr = value != null ? value.ToString() : "null";
                    if (valueStr is { Length: > 100 })
                    {
                        valueStr = string.Concat(valueStr.AsSpan(0, 100), "...");
                    }

                    output.WriteLine($"    {key} = {valueStr}");
                }
            }
            output.WriteLine($"\nTotal debug messages: {debugMessages.Count}");

            // Read console logs
            try
            {
                var consoleLogsExpr = await engine.Evaluate("consoleLogs");
                if (consoleLogsExpr is JsArray consoleLogsArray)
                {
                    output.WriteLine($"\n=== Console Logs ({consoleLogsArray.Length} messages) ===");
                    for (var i = 0; i < Math.Min(50, consoleLogsArray.Length); i++)  // Limit to first 50 for readability
                    {
                        var logEntry = consoleLogsArray.Get(i);
                        output.WriteLine($"[{i}] {logEntry}");
                    }
                    if (consoleLogsArray.Length > 50)
                    {
                        output.WriteLine($"... and {consoleLogsArray.Length - 50} more log entries");
                    }
                }
            }
            catch (Exception logEx)
            {
                output.WriteLine($"Could not read console logs: {logEx.Message}");
            }

            // Re-throw so test fails with details
            throw;
        }
    }

    [Fact(Timeout = 10000, Skip = "Too Slow")]
    public async Task CryptoSha1_Diagnose()
    {
        await using var engine = new JsEngine();

        var content = GetEmbeddedFile("crypto-sha1.js");
        output.WriteLine($"Script length: {content.Length}");

        try
        {
            await engine.Evaluate(content);
            output.WriteLine("Script executed successfully!");
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine($"\nThrowSignal caught!");
            output.WriteLine($"Message: {ex.Message}");
            output.WriteLine($"ThrownValue: {ex.ThrownValue}");

            // Check debug messages
            var debugMessages = new List<DebugMessage>();
            while (engine.DebugMessages().TryRead(out var msg))
            {
                debugMessages.Add(msg);
                output.WriteLine($"\nDebug message {debugMessages.Count}:");
                foreach (var kvp in msg.Variables)
                {
                    var value = kvp.Value;
                    var valueStr = value != null ? value.ToString() : "null";
                    if (valueStr is { Length: > 100 })
                    {
                        valueStr = string.Concat(valueStr.AsSpan(0, 100), "...");
                    }

                    output.WriteLine($"    {kvp.Key} = {valueStr}");
                }
            }

            // Read console logs
            try
            {
                var consoleLogsExpr = await engine.Evaluate("consoleLogs");
                if (consoleLogsExpr is JsArray consoleLogsArray)
                {
                    output.WriteLine($"\n=== Console Logs ({consoleLogsArray.Length} messages) ===");
                    for (var i = 0; i < Math.Min(50, consoleLogsArray.Length); i++)  // Limit to first 50 for readability
                    {
                        var logEntry = consoleLogsArray.Get(i);
                        output.WriteLine($"[{i}] {logEntry}");
                    }
                    if (consoleLogsArray.Length > 50)
                    {
                        output.WriteLine($"... and {consoleLogsArray.Length - 50} more log entries");
                    }
                }
            }
            catch (Exception logEx)
            {
                output.WriteLine($"Could not read console logs: {logEx.Message}");
            }

            throw;
        }
    }

    private static string GetEmbeddedFile(string filename)
    {
        const string resourcePrefix = "Asynkron.JsEngine.Tests.Scripts.";
        var assembly = typeof(SunSpiderDiagnosticTests).Assembly;

        var resourceName = resourcePrefix + filename;
        using var stream = assembly.GetManifestResourceStream(resourceName)
                            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

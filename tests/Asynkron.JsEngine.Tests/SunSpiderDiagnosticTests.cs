using Asynkron.JsEngine;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Diagnostic tests to understand what's failing in SunSpider tests
/// </summary>
public class SunSpiderDiagnosticTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task SimpleThrow_WithStringConcatenation()
    {
        var engine = new JsEngine();
        
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
            output.WriteLine($"ThrownValue type: {ex.ThrownValue?.GetType()}");
            output.WriteLine($"ThrownValue == null: {ex.ThrownValue == null}");
            output.WriteLine($"Message: {ex.Message}");
            
            // This should have a proper error message
            Assert.NotNull(ex.ThrownValue);
            Assert.Contains("ERROR", ex.ThrownValue.ToString());
        }
    }

    [Fact(Timeout = 10000)]
    public async Task CryptoMd5_Diagnose()
    {
        var engine = new JsEngine();
        
        var content = SunSpiderTests.GetEmbeddedFile("crypto-md5.js");
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
            output.WriteLine($"ThrownValue type: {ex.ThrownValue?.GetType()}");
            output.WriteLine($"ThrownValue == null: {ex.ThrownValue == null}");
            
            // Check debug messages
            var debugMessages = new List<DebugMessage>();
            while (engine.DebugMessages().TryRead(out var msg))
            {
                debugMessages.Add(msg);
                output.WriteLine($"\nDebug message {debugMessages.Count}:");
                output.WriteLine($"  Variables count: {msg.Variables.Count}");
                foreach (var kvp in msg.Variables)
                {
                    var value = kvp.Value;
                    var valueStr = value != null ? value.ToString() : "null";
                    if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                    output.WriteLine($"    {kvp.Key} = {valueStr}");
                }
            }
            output.WriteLine($"\nTotal debug messages: {debugMessages.Count}");
            
            // Re-throw so test fails with details
            throw;
        }
    }

    [Fact(Timeout = 10000)]
    public async Task CryptoSha1_Diagnose()
    {
        var engine = new JsEngine();
        
        var content = SunSpiderTests.GetEmbeddedFile("crypto-sha1.js");
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
                    if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                    output.WriteLine($"    {kvp.Key} = {valueStr}");
                }
            }
            
            throw;
        }
    }
}

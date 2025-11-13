using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NBodyExactCopyTest
{
    protected static async Task RunTest(string source)
    {
        var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            Console.WriteLine(args.Count > 0 ? args[0]?.ToString() : string.Empty);
            return null;
        });
        engine.SetGlobalFunction("assert", args =>
        {
            if (args.Count >= 2)
            {
                var condition = args[0];
                var message = args[1]?.ToString() ?? string.Empty;
                Assert.True(condition is true, message);
            }
            return null;
        });
        // Add __debug() function for debugging test scripts
        engine.SetGlobalFunction("__debug", args =>
        {
            // No-op function for debug markers in test scripts
            return null;
        });

        try
        {
            await engine.Evaluate(source);
        }
        catch (ThrowSignal ex)
        {
            // Re-throw with the actual thrown value as the message
            var thrownValue = ex.ThrownValue;
            var message = thrownValue != null ? thrownValue.ToString() : "null";
            throw new Exception($"JavaScript error: {message}", ex);
        }
    }
    
    [Theory]
    [InlineData("access-nbody.js")]
    public async Task AccessNBody_ExactCopy(string filename)
    {
        var content = SunSpiderTests.GetEmbeddedFile(filename);
        await RunTest(content);
    }
}

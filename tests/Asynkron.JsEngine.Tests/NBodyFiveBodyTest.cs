namespace Asynkron.JsEngine.Tests;

public class NBodyFiveBodyTest
{
    [Fact(Timeout = 10000)]
    public async Task FiveBodies_Energy_Works()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("__debug", _ => null);

        var content = SunSpiderTests.GetEmbeddedFile("access-nbody.js");

        await engine.Evaluate(content);
    }

    [Fact]
    public async Task FiveBodies_FullTest_Works()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("__debug", _ => null);

        var content = SunSpiderTests.GetEmbeddedFile("access-nbody.js");

        // Run the script - should throw a ThrowSignal with the expected error
        try
        {
            await engine.Evaluate(content);
            // If we get here, the test passed
            Assert.True(true);
        }
        catch (ThrowSignal ex)
        {
            // JavaScript threw an error - this is a failure
            var message = ex.ThrownValue?.ToString() ?? "null";
            throw new InvalidOperationException($"JavaScript error: {message}", ex);
        }
    }
}

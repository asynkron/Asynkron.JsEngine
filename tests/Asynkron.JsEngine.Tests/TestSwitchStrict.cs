using Asynkron.JsEngine;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class TestSwitchStrictMode
{
    private readonly ITestOutputHelper _output;

    public TestSwitchStrictMode(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SwitchCaseFunctionDeclInStrictMode_ShouldNotHoist()
    {
        var code = System.IO.File.ReadAllText("/tmp/test-strict-switch.js");
        var engine = new JsEngine();
        
        // Redirect console.log to test output
        engine.Evaluate(@"
            var originalLog = console.log;
            console.log = function(...args) {
                // This will be visible in the engine's output
                originalLog(...args);
            };
        ");
        
        try
        {
            engine.Evaluate(code);
            _output.WriteLine("Test completed successfully");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error: {ex.Message}");
            _output.WriteLine($"Stack: {ex.StackTrace}");
            throw;
        }
    }
}

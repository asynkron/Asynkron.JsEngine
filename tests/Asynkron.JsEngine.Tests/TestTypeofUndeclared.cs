using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TestTypeofUndeclared
{
    [Fact]
    public async Task TypeofUndeclaredVariable_ShouldReturnUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undeclaredVar;");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task AccessUndeclaredVariable_ShouldThrow()
    {
        await using var engine = new JsEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("undeclaredVar;");
        });

        Assert.Contains("ReferenceError", ex.Message);
    }
}

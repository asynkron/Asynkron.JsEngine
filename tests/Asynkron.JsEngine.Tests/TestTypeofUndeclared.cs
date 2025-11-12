using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TestTypeofUndeclared
{
    [Fact]
    public async Task TypeofUndeclaredVariable_ShouldReturnUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undeclaredVar;");
        Assert.Equal("undefined", result);
    }
    
    [Fact]
    public async Task AccessUndeclaredVariable_ShouldThrow()
    {
        var engine = new JsEngine();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await engine.Evaluate("undeclaredVar;");
        });
    }
}

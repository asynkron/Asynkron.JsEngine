using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.Regression)]
public sealed class ForInTdzTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ForInConstHead_BindingIsInTdzDuringIterableEvaluation()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                let x = 1;
                for (const x in { x }) { }
                """));

        Assert.Contains("before initialization", ex.Message);
    }

    [Fact]
    public async Task ForInLetHead_BindingIsInTdzDuringIterableEvaluation()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                let x = 1;
                for (let x in { x }) { }
                """));

        Assert.Contains("before initialization", ex.Message);
    }
}

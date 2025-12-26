using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class RestrictedPropertiesQuickTestBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task Generator_HasNoOwnCallerProperty()
    {
        await using var engine = CreateEngine();

        var hasOwn = await engine.Evaluate(@"
            function* generator() {}
            generator.hasOwnProperty('caller');
        ");
        Output.WriteLine($"generator.hasOwnProperty('caller') = {hasOwn}");
        Assert.False((bool)hasOwn!);
    }

    [Fact]
    public async Task Generator_HasNoOwnArgumentsProperty()
    {
        await using var engine = CreateEngine();

        var hasOwn = await engine.Evaluate(@"
            function* generator() {}
            generator.hasOwnProperty('arguments');
        ");
        Output.WriteLine($"generator.hasOwnProperty('arguments') = {hasOwn}");
        Assert.False((bool)hasOwn!);
    }

    [Fact]
    public async Task Generator_CallerAccessThrowsTypeError()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("function* generator() {}");

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("generator.caller;")
        );
        Output.WriteLine($"Thrown: {ex.ThrownValue}");
    }
}

public class FastPathRestrictedPropertiesQuickTest(ITestOutputHelper output) : RestrictedPropertiesQuickTestBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceRestrictedPropertiesQuickTest(ITestOutputHelper output) : RestrictedPropertiesQuickTestBase(output)
{
    protected override bool EnableFastPaths => false;
}

using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibObject)]
public sealed class GlobalUriTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task DecodeURIComponent_DecodesFourByteUtf8Sequence()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("decodeURIComponent('%F0%90%80%80') === String.fromCharCode(0xD800, 0xDC00);");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DecodeURI_DecodesFourByteUtf8Sequence()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("decodeURI('%F0%90%80%80') === String.fromCharCode(0xD800, 0xDC00);");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DecodeURIComponent_RejectsMalformedUtf8Sequences()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const inputs = [
                '%',
                '%80',
                '%FF',
                '%C2',
                '%F0%90%80',
                '%F0%90%80%41',
                '%C0%80',
                '%ED%A0%80',
                '%F4%90%80%80'
            ];
            inputs.every(input => {
                try {
                    decodeURIComponent(input);
                    return false;
                } catch (e) {
                    return e instanceof URIError;
                }
            });
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DecodeURI_RejectsMalformedSingleByteUtf8LeadBytes()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const inputs = ['%80', '%FF', '%C2'];
            inputs.every(input => {
                try {
                    decodeURI(input);
                    return false;
                } catch (e) {
                    return e instanceof URIError;
                }
            });
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DecodeURI_PreservesReservedSingleByteEscapes()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("decodeURI('%2f%3F%23') === '%2f%3F%23';");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DecodeURIComponent_DecodesReservedSingleByteEscapes()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("decodeURIComponent('%2f%3F%23') === '/?#';");

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 4000)]
    public async Task DecodeUri_FourByteLoop_CompletesWithinBoundedTime()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const samples = ['%F0%90%80%80', '%F0%90%80%81', '%F0%90%80%82', '%F0%90%80%83'];
            let total = 0;
            for (let i = 0; i < 20000; i++) {
              const sample = samples[i & 3];
              total += decodeURI(sample).length;
              total += decodeURIComponent(sample).length;
            }
            total === 80000;
            """);

        Assert.True((bool)result!);
    }
}

using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
public sealed class AsyncLoopTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task ForLoop_WithAwaitAndContinue_RejectsExplicitUnifiedBytecodeDecline()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        var decline = await engine.EvaluateAndAwait("""

                                     let result = "";
                                     let asyncResult = undefined;

                                     async function test() {
                                         for (let i = 0; i < 4; i = i + 1) {
                                             if (i === 2) {
                                                 continue;
                                             }

                                             result = result + await __delay(1, i);
                                         }
                                     }

                                     test().then(
                                         () => asyncResult = 'fulfilled:' + result,
                                         error => asyncResult = String(error));
                                     asyncResult;

                         """);

        AssertAsyncFunctionDeclined(decline, "test");
        Assert.Equal("", await engine.Evaluate("result;"));
    }

    [Fact(Timeout = 2000)]
    public async Task DoWhileLoop_WithAwaitAndBreak_RejectsExplicitUnifiedBytecodeDecline()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        var decline = await engine.EvaluateAndAwait("""

                                     let result = "";
                                     let asyncResult = undefined;
                                     let i = 0;

                                     async function test() {
                                         do {
                                             result = result + await __delay(1, i);
                                             if (i === 1) {
                                                 break;
                                             }

                                             i = i + 1;
                                         } while (i < 4);
                                     }

                                     test().then(
                                         () => asyncResult = 'fulfilled:' + result,
                                         error => asyncResult = String(error));
                                     asyncResult;

                         """);

        AssertAsyncFunctionDeclined(decline, "test");
        Assert.Equal("", await engine.Evaluate("result;"));
    }
}

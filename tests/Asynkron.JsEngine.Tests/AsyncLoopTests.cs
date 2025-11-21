using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class AsyncLoopTests
{
    [Fact(Timeout = 2000)]
    public async Task ForLoop_WithAwaitAndContinue_Works()
    {
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""

                                     let result = "";

                                     async function test() {
                                         for (let i = 0; i < 4; i = i + 1) {
                                             if (i === 2) {
                                                 continue;
                                             }

                                             result = result + await __delay(1, i);
                                         }
                                     }

                                     test();

                         """);

        var value = await engine.Evaluate("result;");
        Assert.Equal("013", value);
    }

    [Fact(Timeout = 2000)]
    public async Task DoWhileLoop_WithAwaitAndBreak_Works()
    {
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""

                                     let result = "";
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

                                     test();

                         """);

        var value = await engine.Evaluate("result;");
        Assert.Equal("01", value);
    }
}

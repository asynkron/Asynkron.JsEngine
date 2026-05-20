using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class ClassMethodDestructuringTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task PrivateGeneratorMethod_DefaultArrayRestPattern_BindsRestCopy()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(
            """
            var values = [1, 2, 3];
            var observed = "";
            var callCount = 0;

            var C = class {
              * #method([...x] = values) {
                observed = Array.isArray(x) + "|" + x.length + "|" + (x !== values) + "|" + x.join(",");
                callCount = callCount + 1;
              }

              get method() {
                return this.#method;
              }
            };

            new C().method().next();
            callCount + "|" + observed;
            """);

        Assert.Equal("1|true|3|true|1,2,3", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncPrivateGeneratorMethod_ArrayPatternRestNotFinal_IsParseError()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(
                """
                var C = class {
                  async * #method([...x, y]) {
                  }

                  get method() {
                    return this.#method;
                  }
                };

                new C().method([1, 2, 3]).next();
                """);
        });
    }
}

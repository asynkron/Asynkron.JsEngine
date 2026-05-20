using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class ClassMethodDestructuringTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Method_DefaultArrayPatternAnonymousInitializers_InferBindingNames()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(
            """
            class C {
              method([x = function() {}, y = class {}, z = function*() {}] = []) {
                return x.name + "|" + y.name + "|" + z.name;
              }
            }

            new C().method();
            """);

        Assert.Equal("x|y|z", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateMethod_DefaultObjectRestPattern_SkipsNonEnumerableProperties()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(
            """
            var source = { keep: 1, copied: 2 };
            Object.defineProperty(source, "hidden", { value: 3, enumerable: false });

            class C {
              #method({ keep, ...rest } = source) {
                return keep + "|" + rest.copied + "|" + ("hidden" in rest);
              }

              call() {
                return this.#method();
              }
            }

            new C().call();
            """);

        Assert.Equal("1|2|false", result?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task Method_ArrayPatternDefaultAbruptCompletion_ClosesIterator()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(
            """
            var returnCount = 0;
            var caught = "";
            var iterator = {
              next() {
                return { value: undefined, done: false };
              },
              return() {
                returnCount += 1;
                return {};
              }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            class C {
              method([x = function() { throw new Error("boom"); }()] = iterable) {
              }
            }

            try {
              new C().method();
            } catch (error) {
              caught = error.name + "|" + error.message;
            }

            caught + "|" + returnCount;
            """);

        Assert.Equal("Error|boom|1", result?.ToString());
    }

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

using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
[Category(TestCategories.Regression)]
public sealed class ForAwaitOfScopeResumeTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_LocalIterable_ResumesAfterLoop()
    {
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true });

        var afterLoop = await engine.EvaluateAndAwait("""
            let after = '';

            async function test() {
                let localIterable = {
                    [Symbol.iterator]() {
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                if (index < values.length) {
                                    return { value: values[index++], done: false };
                                }
                                return { done: true };
                            }
                        };
                    }
                };

                let result = '';
                for await (let item of localIterable) {
                    result = result + item;
                }

                after = result;
                return result;
            }

            test();
            after;
            """);

        Assert.Equal("xyz", afterLoop);
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_GlobalIterable_ResumesAfterLoop()
    {
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true });

        await engine.Evaluate("""
            let after = '';

            let globalIterable = {
                [Symbol.iterator]() {
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            if (index < values.length) {
                                return { value: values[index++], done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                let result = '';
                for await (let item of globalIterable) {
                    result = result + item;
                }

                after = result;
                return result;
            }
            """);

        var afterLoop = await engine.EvaluateAndAwait("""
            test();
            after;
            """);

        Assert.Equal("xyz", afterLoop);
    }
}

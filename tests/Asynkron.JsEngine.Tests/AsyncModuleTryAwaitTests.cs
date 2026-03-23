using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ModuleSystem)]
[Category(TestCategories.AsyncRuntime)]
public sealed class AsyncModuleTryAwaitTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task TryCatch_CanEvaluateClassComputedNamesFromAwait()
    {
        await using var engine = CreateEngine();

        await engine.EvaluateModule("""
            globalThis.moduleStatus = "not-run";

            try {
              let C = class {
                [await 9]() { return 9; }
                static [await 9]() { return 9; }
              };

              let c = new C();
              globalThis.moduleStatus = c[await 9]() + C[String(await 9)]();
            } catch (e) {
              globalThis.moduleStatus = e.name + ":" + e.message;
            }
            """);

        Assert.Equal(18.0, engine.GlobalObject["moduleStatus"]);
    }

    [Fact(Timeout = 5000)]
    public async Task TryCatch_CatchesThrowAfterAwaitedExpression()
    {
        await using var engine = CreateEngine();

        await engine.EvaluateModule("""
            globalThis.caughtMessage = "no";

            try {
              let value = await Promise.resolve(1);
              throw new Error(String(value));
            } catch (e) {
              globalThis.caughtMessage = e.message;
            }
            """);

        Assert.Equal("1", engine.GlobalObject["caughtMessage"]);
    }

    [Fact(Timeout = 5000)]
    public async Task TryCatch_CanAssignPropertyFromAwaitedBinaryExpression()
    {
        await using var engine = CreateEngine();

        await engine.EvaluateModule("""
            globalThis.assignmentStatus = "not-run";

            try {
              globalThis.assignmentStatus = 1 + await Promise.resolve(2);
            } catch (e) {
              globalThis.assignmentStatus = e.name + ":" + e.message;
            }
            """);

        Assert.Equal(3.0, engine.GlobalObject["assignmentStatus"]);
    }
}

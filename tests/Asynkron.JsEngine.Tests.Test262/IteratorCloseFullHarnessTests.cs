using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests.Test262;

public sealed class IteratorCloseFullHarnessTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ArrayElemIterReturnCloseNull_FullHarness(bool strict)
    {
        await using var engine = new JsEngine();

        // Mirror BuildTestExecutor harness loading (without full compareArray patch)
        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);

        // Minimal $262 hooks needed for these tests
        var obj262 = new JsObject
        {
            ["createRealm"] = new HostFunction(_ => (JsValue)engine.GlobalObject),
            ["detachArrayBuffer"] = new HostFunction(_ => JsValue.Undefined)
        };
        engine.SetGlobalValue("$262", obj262);

        var source = State.Test262Stream.GetTestFile(
            "language/expressions/assignment/dstr/array-elem-iter-rtrn-close-null.js").Program;

        if (strict && !source.StartsWith("\"use strict\";"))
        {
            source = "\"use strict\";\n" + source;
        }

        var result = await engine.Evaluate(source);

        if (result is JsObject obj &&
            obj.TryGetProperty("caught", out var caughtVal) &&
            obj.TryGetProperty("errName", out var errName))
        {
            TestContext.WriteLine($"caught={caughtVal}, errName={errName}");
        }

        // The test should throw TypeError and be caught by harness assert.throws
        // If we reach here without ThrowSignal, the harness consumed it.
        Assert.Pass("Harness execution completed without unhandled ThrowSignal.");
    }
}

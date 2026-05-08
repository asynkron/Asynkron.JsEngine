using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// TEST BOMB: narrows the object-destructuring failure exposed by Express v5 CommonJS modules.
[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.Integration)]
public sealed class ObjectDestructuringModuleRegressionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    /// H1: global script object destructuring should bind shorthand names.
    [Fact(Timeout = 2000)]
    public async Task H1_GlobalVarObjectDestructuringBindsShorthandNames()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var { METHODS, answer } = { METHODS: ['GET'], answer: 42 };
            METHODS[0] + ':' + answer;
            """);

        Assert.Equal("GET:42", result);
    }

    /// H2: function-body object destructuring should bind shorthand names.
    [Fact(Timeout = 2000)]
    public async Task H2_FunctionVarObjectDestructuringBindsShorthandNames()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function run() {
                var { METHODS, answer } = { METHODS: ['GET'], answer: 42 };
                return METHODS[0] + ':' + answer;
            }
            run();
            """);

        Assert.Equal("GET:42", result);
    }

    /// H3: CommonJS-style wrapper invocation should preserve object destructuring bindings.
    [Fact(Timeout = 2000)]
    public async Task H3_CommonJsWrapperObjectDestructuringBindsShorthandNames()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var factory = (function (exports, require, module, __filename, __dirname) {
                var { METHODS, answer } = require('node:http');
                module.exports = METHODS[0] + ':' + answer;
            });

            var module = { exports: {} };
            factory({}, function () { return { METHODS: ['GET'], answer: 42 }; }, module, 'index.js', '.');
            module.exports;
            """);

        Assert.Equal("GET:42", result);
    }

    /// H4: object destructuring from a host-created JsObject should read its properties.
    [Fact(Timeout = 2000)]
    public async Task H4_HostObjectDestructuringBindsShorthandNames()
    {
        await using var engine = CreateEngine();
        var module = new JsObject();
        var methods = new JsArray(engine.RealmState);
        methods.SetElement(0, "GET");
        module.SetProperty("METHODS", JsValue.FromObjectUnsafe(methods));
        module.SetProperty("answer", 42d);
        engine.SetGlobalValue("hostModule", module);

        var result = await engine.Evaluate("""
            var { METHODS, answer } = hostModule;
            METHODS[0] + ':' + answer;
            """);

        Assert.Equal("GET:42", result);
    }
}

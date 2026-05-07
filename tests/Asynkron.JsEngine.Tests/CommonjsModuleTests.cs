using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ModuleSystem)]
[Category(TestCategories.Integration)]
public sealed class CommonjsModuleTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task CreateCommonjsModule_FunctionIsCallable()
    {
        const string script = @"
function createCommonjsModule(fn, basedir, module) {
    return module = {
        path: basedir,
        exports: {},
        require: function (path, base) {
            return null;
        }
    }, fn(module, module.exports), module.exports;
}

var browser$5 = createCommonjsModule(function (module, exports) {
    exports.ok = true;
});

var result = browser$5.ok === true;
";

        await using var engine = CreateEngine();
        await engine.Evaluate(script);

        var result = await engine.Evaluate("result;") as bool?;
        Assert.True(result);
    }

    [Fact(Timeout = 2000)]
    public async Task CreateCommonjsModule_FunctionIsHoistedInsideFactory()
    {
        const string script = @"
function factory() {
    function createCommonjsModule(fn, basedir, module) {
        return module = {
            path: basedir,
            exports: {},
            require: function (path, base) {
                return null;
            }
        }, fn(module, module.exports), module.exports;
    }

    var browser$5 = createCommonjsModule(function (module, exports) {
        exports.ok = true;
    });

    return browser$5.ok === true;
}

var result = factory();
";

        await using var engine = CreateEngine();
        await engine.Evaluate(script);

        var result = await engine.Evaluate("result;") as bool?;
        Assert.True(result);
    }

    [Fact(Timeout = 2000)]
    public async Task HostInvokedCommonjsWrapper_PreservesExportedFunctionPrototypeAssignments()
    {
        const string source = """
            'use strict';

            module.exports = Route;

            function Route(path) {
                this.path = path;
                this.stack = [];
                this.methods = {};
            }

            Route.prototype._handles_method = function _handles_method(method) {
                return Boolean(this.methods[method]);
            };

            Route.prototype._options = function _options() {
                var methods = Object.keys(this.methods);

                for (var i = 0; i < methods.length; i++) {
                    methods[i] = methods[i].toUpperCase();
                }

                return methods;
            };

            Route.prototype.dispatch = function dispatch(req, res, done) {
                var idx = 0;
                var stack = this.stack;
                var sync = 0;

                if (stack.length === 0) {
                    return done();
                }

                next();

                function next(err) {
                    if (++sync > 100) {
                        return setImmediate(next, err);
                    }

                    var layer = stack[idx++];
                    if (!layer) {
                        return done(err);
                    }
                }
            };
            """;

        var wrappedSource =
            "(function (exports, require, module, __filename, __dirname) {\n" +
            source +
            "\n})";

        await using var engine = CreateEngine();
        var factoryValue = JsValue.FromObjectUnsafe(engine.EvaluateSync(wrappedSource));
        Assert.True(factoryValue.TryGetCallable(out var factory));

        var exports = new JsObject();
        var module = new JsObject();
        module.DefineProperty("exports", new PropertyDescriptor
        {
            JsValue = exports,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        factory.Invoke(
            [
                exports,
                JsValue.Undefined,
                module,
                "route.js",
                "."
            ],
            JsValue.Undefined);

        Assert.True(module.TryGetProperty("exports", out var moduleExports));
        engine.SetGlobalValue("Route", moduleExports);

        var result = await engine.Evaluate("""
            var route = new Route('/');
            [
                typeof Route.prototype._handles_method,
                typeof Route.prototype._options,
                typeof Route.prototype.dispatch,
                typeof route.dispatch,
                Object.getPrototypeOf(route) === Route.prototype
            ].join('|');
            """);

        Assert.Equal("function|function|function|function|true", result);
    }
}

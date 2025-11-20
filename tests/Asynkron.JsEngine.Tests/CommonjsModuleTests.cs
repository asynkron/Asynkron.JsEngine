using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class CommonjsModuleTests
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

        await using var engine = new JsEngine();
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

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var result = await engine.Evaluate("result;") as bool?;
        Assert.True(result);
    }
}

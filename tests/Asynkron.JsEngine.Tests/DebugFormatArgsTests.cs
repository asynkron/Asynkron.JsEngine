using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class DebugFormatArgsTests
{
    [Fact(Timeout = 2000)]
    public async Task SetupCopiesFormatArgsOntoCreateDebug()
    {
        const string script = """
            function setup(env) {
              function createDebug(namespace) {
                function debug() {}
                debug.namespace = namespace;
                debug.log = function () {};
                return debug;
              }

              createDebug.debug = createDebug;
              createDebug["default"] = createDebug;

              Object.keys(env).forEach(function (key) {
                createDebug[key] = env[key];
              });

              return createDebug;
            }

            function formatArgs(args) {
              return args;
            }

            var exports = {};
            exports.formatArgs = formatArgs;

            var createDebug = setup(exports);
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var typeOfFormatArgs = await engine.Evaluate("typeof createDebug.formatArgs;");
        Assert.Equal("function", typeOfFormatArgs);
    }
}


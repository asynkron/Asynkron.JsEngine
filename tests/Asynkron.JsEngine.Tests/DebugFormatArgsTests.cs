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

    [Fact(Timeout = 2000)]
    public async Task Setup_Enable_Uses_Load_Result()
    {
        const string script = """
            let calledWith = null;

            function setup(env) {
              function save(namespaces) {}
              function load() { return "ns"; }
              function useColors() { return false; }
              function formatArgs(args) { return args; }

              function createDebug(namespace) {
                function debug() {}
                debug.namespace = namespace;
                debug.log = function () {};
                return debug;
              }

              function enable(namespaces) {
                calledWith = namespaces;
              }

              function disable() {}
              function enabled(name) { return true; }

              createDebug.debug = createDebug;
              createDebug["default"] = createDebug;
              createDebug.coerce = function (v) { return v; };
              createDebug.disable = disable;
              createDebug.enable = enable;
              createDebug.enabled = enabled;
              createDebug.humanize = function (ms) { return String(ms); };

              Object.keys(env).forEach(function (key) {
                createDebug[key] = env[key];
              });

              createDebug.names = [];
              createDebug.skips = [];
              createDebug.formatters = {};

              createDebug.enable(createDebug.load());
              return createDebug;
            }

            var exports = {};
            exports.formatArgs = function (args) { return args; };
            exports.save = function (namespaces) {};
            exports.load = function () { return "ns"; };
            exports.useColors = function () { return false; };

            var dbg = setup(exports);
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var calledWith = await engine.Evaluate("calledWith;");
        Assert.Equal("ns", calledWith);
    }
}

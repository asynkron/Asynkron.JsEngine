using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Diagnostic tests to understand what's failing in SunSpider tests
/// </summary>
public class SunSpiderDiagnosticTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task SimpleThrow_WithStringConcatenation()
    {
        await using var engine = new JsEngine();

        try
        {
            await engine.Evaluate(@"
                var expected = 'hello';
                var actual = 'world';
                throw 'ERROR: expected ' + expected + ' but got ' + actual;
            ");
            output.WriteLine("No exception thrown - UNEXPECTED");
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine($"ThrownValue: '{ex.ThrownValue}'");
            output.WriteLine($"ThrownValue type: {ex.ThrownValue?.GetType()}");
            output.WriteLine($"ThrownValue == null: {ex.ThrownValue == null}");
            output.WriteLine($"Message: {ex.Message}");

            // This should have a proper error message
            Assert.NotNull(ex.ThrownValue);
            Assert.Contains("ERROR", ex.ThrownValue.ToString());
        }
    }

    [Fact(Timeout = 10000)]
    public async Task CryptoMd5_Diagnose()
    {
        await using var engine = new JsEngine();

        var content = SunSpiderTests.GetEmbeddedFile("crypto-md5.js");
        output.WriteLine($"Script length: {content.Length}");

        try
        {
            await engine.Evaluate(content);
            output.WriteLine("Script executed successfully!");
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine($"\nThrowSignal caught!");
            output.WriteLine($"Message: {ex.Message}");
            output.WriteLine($"ThrownValue: {ex.ThrownValue}");
            output.WriteLine($"ThrownValue type: {ex.ThrownValue?.GetType()}");
            output.WriteLine($"ThrownValue == null: {ex.ThrownValue == null}");

            // Check debug messages
            var debugMessages = new List<DebugMessage>();
            while (engine.DebugMessages().TryRead(out var msg))
            {
                debugMessages.Add(msg);
                output.WriteLine($"\nDebug message {debugMessages.Count}:");
                output.WriteLine($"  Variables count: {msg.Variables.Count}");
                foreach (var (key, value) in msg.Variables)
                {
                    var valueStr = value != null ? value.ToString() : "null";
                    if (valueStr is { Length: > 100 })
                    {
                        valueStr = string.Concat(valueStr.AsSpan(0, 100), "...");
                    }

                    output.WriteLine($"    {key} = {valueStr}");
                }
            }
            output.WriteLine($"\nTotal debug messages: {debugMessages.Count}");

            // Read console logs
            try
            {
                var consoleLogsExpr = await engine.Evaluate("consoleLogs");
                if (consoleLogsExpr is JsArray consoleLogsArray)
                {
                    output.WriteLine($"\n=== Console Logs ({consoleLogsArray.Length} messages) ===");
                    for (var i = 0; i < Math.Min(50, consoleLogsArray.Length); i++)  // Limit to first 50 for readability
                    {
                        var logEntry = consoleLogsArray.Get(i);
                        output.WriteLine($"[{i}] {logEntry}");
                    }
                    if (consoleLogsArray.Length > 50)
                    {
                        output.WriteLine($"... and {consoleLogsArray.Length - 50} more log entries");
                    }
                }
            }
            catch (Exception logEx)
            {
                output.WriteLine($"Could not read console logs: {logEx.Message}");
            }

            // Re-throw so test fails with details
            throw;
        }
    }

    [Fact(Timeout = 10000)]
    public async Task CryptoSha1_Diagnose()
    {
        await using var engine = new JsEngine();

        var content = SunSpiderTests.GetEmbeddedFile("crypto-sha1.js");
        output.WriteLine($"Script length: {content.Length}");

        try
        {
            await engine.Evaluate(content);
            output.WriteLine("Script executed successfully!");
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine($"\nThrowSignal caught!");
            output.WriteLine($"Message: {ex.Message}");
            output.WriteLine($"ThrownValue: {ex.ThrownValue}");

            // Check debug messages
            var debugMessages = new List<DebugMessage>();
            while (engine.DebugMessages().TryRead(out var msg))
            {
                debugMessages.Add(msg);
                output.WriteLine($"\nDebug message {debugMessages.Count}:");
                foreach (var kvp in msg.Variables)
                {
                    var value = kvp.Value;
                    var valueStr = value != null ? value.ToString() : "null";
                    if (valueStr is { Length: > 100 })
                    {
                        valueStr = string.Concat(valueStr.AsSpan(0, 100), "...");
                    }

                    output.WriteLine($"    {kvp.Key} = {valueStr}");
                }
            }

            // Read console logs
            try
            {
                var consoleLogsExpr = await engine.Evaluate("consoleLogs");
                if (consoleLogsExpr is JsArray consoleLogsArray)
                {
                    output.WriteLine($"\n=== Console Logs ({consoleLogsArray.Length} messages) ===");
                    for (var i = 0; i < Math.Min(50, consoleLogsArray.Length); i++)  // Limit to first 50 for readability
                    {
                        var logEntry = consoleLogsArray.Get(i);
                        output.WriteLine($"[{i}] {logEntry}");
                    }
                    if (consoleLogsArray.Length > 50)
                    {
                        output.WriteLine($"... and {consoleLogsArray.Length - 50} more log entries");
                    }
                }
            }
            catch (Exception logEx)
            {
                output.WriteLine($"Could not read console logs: {logEx.Message}");
            }

            throw;
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DateFormat_Eval_DefinesCallablePrototypeMethod()
    {
        const string script = """
            Date.parseFunctions = {count:0};
            Date.parseRegexes = [];
            Date.formatFunctions = {count:0};

            // Minimal helpers required by the date-format-xparb.js snippet
            String.escape = function(ch) { return ch; };
            Date.getFormatCode = function(character) { return "'X' + "; };

            Date.createNewFormat = function(format) {
                var funcName = "format" + Date.formatFunctions.count++;
                Date.formatFunctions[format] = funcName;
                var code = "Date.prototype." + funcName + " = function(){return ";
                var special = false;
                var ch = '';
                for (var i = 0; i < format.length; ++i) {
                    ch = format.charAt(i);
                    if (!special && ch == "\\") {
                        special = true;
                    }
                    else if (special) {
                        special = false;
                        code += "'" + String.escape(ch) + "' + ";
                    }
                    else {
                        code += Date.getFormatCode(ch);
                    }
                }
                eval(code.substring(0, code.length - 3) + ";}");
            };

            Date.createNewFormat("d");

            var formatKey = "d";
            var funcName = Date.formatFunctions[formatKey];
            globalFunc = funcName;
            globalDate = new Date(0);
            globalMethod = globalDate[funcName];
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var funcName = await engine.Evaluate("globalFunc;");
        Assert.IsType<string>(funcName);

        var method = await engine.Evaluate("globalMethod;");
        Assert.NotNull(method);
        Assert.IsAssignableFrom<IJsCallable>(method);
    }

    [Fact(Timeout = 5000)]
    public async Task DateFormat_SimpleRepro_DoesNotUseSymbolAsCallee()
    {
        const string script = """
            Date.parseFunctions = {count:0};
            Date.parseRegexes = [];
            Date.formatFunctions = {count:0};

            String.escape = function(ch) { return ch; };
            Date.getFormatCode = function(character) {
                if (character === "d") {
                    return "'DAY' + ";
                }
                return "'' + ";
            };

            Date.createNewFormat = function(format) {
                var funcName = "format" + Date.formatFunctions.count++;
                Date.formatFunctions[format] = funcName;
                var code = "Date.prototype." + funcName + " = function(){return ";
                var special = false;
                var ch = '';
                for (var i = 0; i < format.length; ++i) {
                    ch = format.charAt(i);
                    if (!special && ch == "\\") {
                        special = true;
                    }
                    else if (special) {
                        special = false;
                        code += "'" + String.escape(ch) + "' + ";
                    }
                    else {
                        code += Date.getFormatCode(ch);
                    }
                }
                eval(code.substring(0, code.length - 3) + ";}");
            };

            Date.prototype.dateFormat = function(format) {
                if (Date.formatFunctions[format] == null) {
                    Date.createNewFormat(format);
                }
                var func = Date.formatFunctions[format];
                return this[func]();
            };

            var result = new Date(0).dateFormat("d");
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);
    }
}

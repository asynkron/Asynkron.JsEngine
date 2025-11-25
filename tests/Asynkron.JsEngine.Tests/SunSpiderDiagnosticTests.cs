using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Diagnostic tests to understand what's failing in SunSpider tests
/// </summary>
public class SunSpiderDiagnosticTests(ITestOutputHelper output)
{
    [Fact(Timeout = 60000)]
    public async Task SimpleThrow_WithStringConcatenation()
    {
        await using var engine = new JsEngine();
        engine.ExecutionTimeout = TimeSpan.FromMinutes(2);

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

    [Fact(Timeout = 60000)]
    public async Task CryptoMd5_Diagnose()
    {
        await using var engine = new JsEngine();
        engine.ExecutionTimeout = TimeSpan.FromMinutes(2);

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

    [Fact(Timeout = 60000)]
    public async Task CryptoSha1_Diagnose()
    {
        await using var engine = new JsEngine();
        engine.ExecutionTimeout = TimeSpan.FromMinutes(2);

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

    [Fact(Timeout = 60000)]
    public async Task StringTagcloud_Diagnose()
    {
        await using var engine = new JsEngine();
        engine.ExecutionTimeout = TimeSpan.FromMinutes(2);

        var content = SunSpiderTests.GetEmbeddedFile("string-tagcloud.js");

        var marker = "if (tagcloud.length < expectedMinLength)";
        var idx = content.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            content = content[..idx] + @"
var __tagcloudLength = tagcloud.length;
var __anchorCount = (tagcloud.match(/<a /g) || []).length;
";
        }

        await engine.Evaluate(content);

        var length = await engine.Evaluate("__tagcloudLength;");
        var anchors = await engine.Evaluate("__anchorCount;");

        output.WriteLine("tagcloud.length = " + length);
        output.WriteLine("anchorCount = " + anchors);
    }

    [Fact(Timeout = 10000)]
    public async Task CryptoAes_Diagnose_CoreVsEscaping()
    {
        await using var engine = new JsEngine();

        var content = SunSpiderTests.GetEmbeddedFile("crypto-aes.js");

        try
        {
            await engine.Evaluate(content);

            // Override escCtrlChars/unescCtrlChars at runtime with identity
            // versions so we can inspect the AES core + CTR behaviour without
            // any escaping effects.
            const string diagScript = """
__diagPlainText = plainText;
__diagPassword = password;

__origEscCtrlChars = escCtrlChars;
__origUnescCtrlChars = unescCtrlChars;

escCtrlChars = function(str) { return str; };
unescCtrlChars = function(str) { return str; };

__diagCipherText = AESEncryptCtr(__diagPlainText, __diagPassword, 256);
__diagDecryptedText = AESDecryptCtr(__diagCipherText, __diagPassword, 256);

// restore originals in case anything else depends on them
escCtrlChars = __origEscCtrlChars;
unescCtrlChars = __origUnescCtrlChars;
""";

            await engine.Evaluate(diagScript);
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine("ThrowSignal: " + ex.ThrownValue);
        }

        var diagCipher = await engine.Evaluate("__diagCipherText;");
        var diagPlain = await engine.Evaluate("plainText;");
        var diagDecrypted = await engine.Evaluate("__diagDecryptedText;");

        output.WriteLine("plainText length   = " + diagPlain?.ToString()?.Length);
        output.WriteLine("cipherText length  = " + diagCipher?.ToString()?.Length);
        output.WriteLine("decryptedText len  = " + diagDecrypted?.ToString()?.Length);

        // Log the first 80 chars of decrypted text for visual comparison.
        await engine.Evaluate(@"
            __diagDecryptedHead = __diagDecryptedText.substring(0, 80);
        ");
        var head = await engine.Evaluate("__diagDecryptedHead;");
        output.WriteLine("decryptedText head = " + head);
    }

    [Fact(Timeout = 60000, Skip = "Investigative test")]
    public async Task Babel_Debug_Diagnose_CreateDebugEnableLoad()
    {
        await using var engine = new JsEngine();

        var content = SunSpiderTests.GetEmbeddedFile("babel-standalone.js");

        try
        {
            await engine.Evaluate(content);
        }
        catch (ThrowSignal ex)
        {
            output.WriteLine("ThrowSignal: " + ex.ThrownValue);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task Babel_Debug_Minimal_Setup_PopulatesFormatters()
    {
        const string script = """
            function setup(env) {
                function createDebug(namespace) {
                    function debug() { }
                    return debug;
                }

                createDebug.debug = createDebug;
                createDebug.enable = function (namespaces) { };
                createDebug.load = function () { return ""; };
                createDebug.formatters = {};

                return createDebug;
            }

            function common(env) { return setup(env); }

            (function () {
                var module = { exports: {} };
                var exports = module.exports;

                exports.formatArgs = function formatArgs() { };
                exports.save = function save() { };
                exports.load = function load() { };
                exports.useColors = function useColors() { return false; };
                exports.storage = {};
                exports.destroy = function destroy() { };
                exports.colors = [];

                module.exports = common(exports);
                var formatters = module.exports.formatters;
                formatters.j = function (v) { return JSON.stringify(v); };

                globalThis.__diagTypeModuleExports = typeof module.exports;
                globalThis.__diagTypeFormatters = typeof module.exports.formatters;
                globalThis.__diagTypeFormatterJ = typeof module.exports.formatters.j;
            })();
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var typeModuleExports = await engine.Evaluate("globalThis.__diagTypeModuleExports;");
        var typeFormatters = await engine.Evaluate("globalThis.__diagTypeFormatters;");
        var typeFormatterJ = await engine.Evaluate("globalThis.__diagTypeFormatterJ;");

        Assert.Equal("function", typeModuleExports);
        Assert.Equal("object", typeFormatters);
        Assert.Equal("function", typeFormatterJ);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteral_Inherits_ObjectPrototype()
    {
        const string script = """
            var obj = { foo: 1 };
            globalThis.__diagHasOwnType = typeof obj.hasOwnProperty;
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var hasOwnType = await engine.Evaluate("globalThis.__diagHasOwnType;");
        Assert.Equal("function", hasOwnType);
    }

    [Fact(Timeout = 2000)]
    public async Task ForIn_HasOwnProperty_Guard_Works()
    {
        const string script = """
            var colorName = { red: [255, 0, 0], green: [0, 255, 0] };
            var reverse = {};
            for (var key in colorName) {
                if (colorName.hasOwnProperty(key)) {
                    reverse[colorName[key]] = key;
                }
            }
            globalThis.__diagReverseKeyCount = Object.keys(reverse).length;
            """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var count = await engine.Evaluate("globalThis.__diagReverseKeyCount;");
        Assert.Equal(2d, count);
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

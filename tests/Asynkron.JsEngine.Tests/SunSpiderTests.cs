using System.Collections.Generic;
using System.Reflection;
using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// SunSpider benchmark tests. See SUNSPIDER_TEST_FINDINGS.md for detailed analysis of known failures.
/// Current expectations are tracked per script via the <c>shouldSucceed</c> flag.
/// </summary>
public class SunSpiderTests
{
    private const string ScriptResourcePrefix = "Asynkron.JsEngine.Tests.Scripts.";

    private static readonly HashSet<string> HighBudgetScripts = new(StringComparer.Ordinal)
    {
        "access-fannkuch.js",
        "string-validate-input.js"
    };

    [Theory(Timeout = 4000)]
    // Passing scenarios (23)
    [InlineData("3d-cube.js", true)]
    [InlineData("3d-morph.js", true)]
    [InlineData("3d-raytrace.js", true)]
    [InlineData("access-binary-trees.js", true)]
    [InlineData("access-fannkuch.js", true)]
    [InlineData("access-nbody.js", true)]
    [InlineData("access-nsieve.js", true)]
    [InlineData("bitops-3bit-bits-in-byte.js", true)]
    [InlineData("bitops-bits-in-byte.js", true)]
    [InlineData("bitops-bitwise-and.js", true)]
    [InlineData("bitops-nsieve-bits.js", true)]
    //[InlineData("controlflow-recursive.js", false)]
    [InlineData("crypto-md5.js", true)]
    [InlineData("crypto-sha1.js", true)]
    [InlineData("date-format-tofte.js", true)]
    [InlineData("math-cordic.js", true)]
    [InlineData("math-partial-sums.js", true)]
    [InlineData("math-spectral-norm.js", true)]
    [InlineData("regexp-dna.js", true)]
    [InlineData("string-base64.js", true)]
    [InlineData("string-fasta.js", true)]
    [InlineData("string-unpack-code.js", true)]
    [InlineData("string-validate-input.js", true)]
    // Known failures (4) - keep running so we notice improvements when they start passing again.
  //  [InlineData("babel-standalone.js", false)] // Parser fails with a complex ternary expression.
    [InlineData("crypto-aes.js", false)] // AES bit-manipulation still misbehaves.
    [InlineData("date-format-xparb.js", false)] // Date formatting discrepancies.
 //   [InlineData("string-tagcloud.js", false)] // Parser still rejects valid syntax.
    public async Task SunSpider_Scripts_behave_as_expected(string filename, bool shouldSucceed)
    {
        var content = GetEmbeddedFile(filename);
        var timeout = HighBudgetScripts.Contains(filename)
            ? TimeSpan.FromSeconds(4)
            : TimeSpan.FromSeconds(3);
        var exception = await Record.ExceptionAsync(() => RunTest(content).WaitAsync(timeout));

        if (shouldSucceed)
        {
            Assert.True(exception is null, $"{filename} is expected to run without errors.");
        }
        else
        {
            Assert.True(exception is not null, $"{filename} currently fails and should keep surfacing the issue until fixed.");
        }
    }

    private static async Task RunTest(string source)
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            Console.WriteLine(args.Count > 0 ? args[0]?.ToString() : string.Empty);
            return null;
        });
        engine.SetGlobalFunction("assert", args =>
        {
            if (args.Count >= 2)
            {
                var condition = args[0];
                var message = args[1]?.ToString() ?? string.Empty;
                Assert.True(condition is true, message);
            }
            return null;
        });
        // Add __debug() function for debugging test scripts.
        engine.SetGlobalFunction("__debug", _ => null);

        try
        {
            await engine.Evaluate(source).ConfigureAwait(false);
        }
        catch (ThrowSignal ex)
        {
            // Re-throw with the actual thrown value as the message for better diagnostics.
            var thrownValue = ex.ThrownValue;
            var message = thrownValue != null ? thrownValue.ToString() : "null";
            throw new Exception($"JavaScript error: {message}", ex);
        }
    }

    internal static string GetEmbeddedFile(string filename)
    {
        var assembly = typeof(SunSpiderTests).GetTypeInfo().Assembly;
        var scriptPath = ScriptResourcePrefix + filename;

        using var stream = assembly.GetManifestResourceStream(scriptPath);
        if (stream == null)
        {
            throw new FileNotFoundException($"Could not find embedded resource: {scriptPath}");
        }

        using var sr = new StreamReader(stream);
        return sr.ReadToEnd();
    }
}

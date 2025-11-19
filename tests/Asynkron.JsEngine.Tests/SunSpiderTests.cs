using System.Reflection;

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
    [InlineData("3d-cube.js")]
    [InlineData("3d-morph.js")]
    [InlineData("3d-raytrace.js")]
    [InlineData("access-binary-trees.js")]
    [InlineData("access-fannkuch.js")]
    [InlineData("access-nbody.js")]
    [InlineData("access-nsieve.js")]
    [InlineData("bitops-3bit-bits-in-byte.js")]
    [InlineData("bitops-bits-in-byte.js")]
    [InlineData("bitops-bitwise-and.js")]
    [InlineData("bitops-nsieve-bits.js")]
    [InlineData("controlflow-recursive.js")]
    [InlineData("crypto-md5.js")]
    [InlineData("crypto-sha1.js")]
    [InlineData("date-format-tofte.js")]
    [InlineData("math-cordic.js")]
    [InlineData("math-partial-sums.js")]
    [InlineData("math-spectral-norm.js")]
    [InlineData("regexp-dna.js")]
    [InlineData("string-base64.js")]
    [InlineData("string-fasta.js")]
    [InlineData("string-unpack-code.js")]
    [InlineData("string-validate-input.js")]
    [InlineData("babel-standalone.js")]
    [InlineData("crypto-aes.js")]
    [InlineData("date-format-xparb.js")]
    [InlineData("string-tagcloud.js")]
    public async Task SunSpider_Scripts_behave_as_expected(string filename)
    {
        var content = GetEmbeddedFile(filename);
        var timeout = HighBudgetScripts.Contains(filename)
            ? TimeSpan.FromSeconds(4)
            : TimeSpan.FromSeconds(3);
        await RunTest(content).WaitAsync(timeout);
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

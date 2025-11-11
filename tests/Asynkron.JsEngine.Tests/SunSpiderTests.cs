using System.Reflection;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class SunSpiderTests
{
    private static async Task RunTest(string source)
    {
        var engine = new JsEngine();
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

        await engine.Evaluate(source);
    }

    [Theory]
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
    //ß[InlineData("controlflow-recursive.js")]
    [InlineData("crypto-aes.js")]
    [InlineData("crypto-md5.js")]
    [InlineData("crypto-sha1.js")]
    [InlineData("date-format-tofte.js")]
    [InlineData("date-format-xparb.js")]
    [InlineData("math-cordic.js")]
    [InlineData("math-partial-sums.js")]
    [InlineData("math-spectral-norm.js")]
    [InlineData("regexp-dna.js")]
    [InlineData("string-base64.js")]
    [InlineData("string-fasta.js")]
    [InlineData("string-tagcloud.js")]
    [InlineData("string-unpack-code.js")]
    [InlineData("string-validate-input.js")]
    [InlineData("babel-standalone.js")]
    public async Task Sunspider(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    internal static string GetEmbeddedFile(string filename)
    {
        const string Prefix = "Asynkron.JsEngine.Tests.Scripts.";

        var assembly = typeof(SunSpiderTests).GetTypeInfo().Assembly;
        var scriptPath = Prefix + filename;

        using var stream = assembly.GetManifestResourceStream(scriptPath);
        if (stream == null)
        {
            throw new FileNotFoundException($"Could not find embedded resource: {scriptPath}");
        }
        using var sr = new StreamReader(stream);
        return sr.ReadToEnd();
    }
}

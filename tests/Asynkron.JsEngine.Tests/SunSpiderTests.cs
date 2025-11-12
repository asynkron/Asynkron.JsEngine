using System.Reflection;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// SunSpider benchmark tests. See SUNSPIDER_TEST_FINDINGS.md for detailed analysis of failures.
/// Current status: 10 passing / 16 failing
/// </summary>
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

        try
        {
            await engine.Evaluate(source);
        }
        catch (ThrowSignal ex)
        {
            // Re-throw with the actual thrown value as the message
            var thrownValue = ex.ThrownValue;
            var message = thrownValue != null ? thrownValue.ToString() : "null";
            throw new Exception($"JavaScript error: {message}", ex);
        }
    }

    // ====================================================================================
    // PASSING TESTS (10)
    // ====================================================================================

    /// <summary>
    /// 3D rendering tests that are passing
    /// </summary>
    [Theory]
    [InlineData("3d-morph.js")]
    public async Task SunSpider_3D_Passing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Array access tests that are passing
    /// </summary>
    [Theory]
    [InlineData("access-binary-trees.js")]
    [InlineData("access-nsieve.js")]
    public async Task SunSpider_Access_Passing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Bitwise operation tests that are passing
    /// </summary>
    [Theory]
    [InlineData("bitops-3bit-bits-in-byte.js")]
    [InlineData("bitops-bits-in-byte.js")]
    [InlineData("bitops-bitwise-and.js")]
    [InlineData("bitops-nsieve-bits.js")]
    public async Task SunSpider_Bitops_Passing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Math tests that are passing
    /// </summary>
    [Theory]
    [InlineData("math-cordic.js")]
    [InlineData("math-partial-sums.js")]
    [InlineData("math-spectral-norm.js")]
    public async Task SunSpider_Math_Passing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    // ====================================================================================
    // FAILING TESTS - PARSE ERRORS (6)
    // See SUNSPIDER_TEST_FINDINGS.md for detailed analysis
    // ====================================================================================

    /// <summary>
    /// Parse error: Ternary operator with assignments in both branches
    /// Error: Invalid assignment target near line 15 column 44
    /// Issue: (k%2)?email=username+"@mac.com":email=username+"(at)mac.com"
    /// Root Cause: Parser doesn't support assignments as expressions in ternary operators
    /// </summary>
    [Theory(Skip = "Parse error: Invalid assignment in ternary operator - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("string-validate-input.js")]
    public async Task SunSpider_ParseError_TernaryAssignment(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Parse error: Unexpected token Semicolon at line 200, column 64
    /// Context: ... Q.Line[0] = true; };
    /// Root Cause: Parser issue with complex expression / empty statement after closing brace
    /// </summary>
    [Theory(Skip = "Parse error: Unexpected semicolon - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("3d-cube.js")]
    public async Task SunSpider_ParseError_Semicolon(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Parse errors: Expected ';' after expression statement
    /// Root Cause: ASI (Automatic Semicolon Insertion) not handling newlines correctly
    /// </summary>
    [Theory(Skip = "Parse error: ASI issue - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("string-tagcloud.js")]
    [InlineData("regexp-dna.js")]
    public async Task SunSpider_ParseError_ASI(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Parse error: Unexpected token Semicolon in minified/packed code
    /// Error at line 18, column 268
    /// Root Cause: Complex semicolon placement in minified code
    /// </summary>
    [Theory(Skip = "Parse error: Minified code issue - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("string-unpack-code.js")]
    public async Task SunSpider_ParseError_MinifiedCode(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Parse error: Expected ')' after expression at line 62, column 78
    /// Context: ...his : global || self, factory(global.Bab...
    /// Root Cause: Complex expression parsing issue with ternary operator
    /// </summary>
    [Theory(Skip = "Parse error: Complex expression - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("babel-standalone.js")]
    public async Task SunSpider_ParseError_ComplexExpression(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    // ====================================================================================
    // FAILING TESTS - RUNTIME ERRORS: CRYPTOGRAPHIC (3)
    // See SUNSPIDER_TEST_FINDINGS.md for detailed analysis
    // ====================================================================================

    /// <summary>
    /// Runtime error: MD5 hash calculation produces incorrect result
    /// Expected: a831e91e0f70eddcb70dc61c6f82f6cd
    /// Got: 4ebea80adf00ebd69b1e70e54a6f194a
    /// Root Cause: Likely bit operation or integer overflow issue
    /// - Bitwise operations (shifts, rotations)
    /// - Integer arithmetic (32-bit wrap-around)
    /// - Endianness handling
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect MD5 hash - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("crypto-md5.js")]
    public async Task SunSpider_Crypto_MD5_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Runtime error: SHA1 hash calculation produces incorrect result
    /// Expected: 2524d264def74cce2498bf112bedf00e6c0b796d
    /// Got: 85634b6b67255134eeb5fd1c9b02f4bf0481b7c4
    /// Root Cause: Similar to MD5 - bit operations or integer handling
    /// - Bitwise rotations (rol operations)
    /// - 32-bit unsigned integer arithmetic
    /// - Proper handling of large numbers
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect SHA1 hash - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("crypto-sha1.js")]
    public async Task SunSpider_Crypto_SHA1_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Runtime error: AES encryption/decryption produces incorrect results
    /// Root Cause: Complex bit operations in AES algorithm
    /// - Byte-level operations
    /// - Bit shifting and masking
    /// - S-box lookups
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect AES encryption - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("crypto-aes.js")]
    public async Task SunSpider_Crypto_AES_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    // ====================================================================================
    // FAILING TESTS - RUNTIME ERRORS: NUMERICAL CALCULATIONS (2)
    // See SUNSPIDER_TEST_FINDINGS.md for detailed analysis
    // ====================================================================================

    /// <summary>
    /// Runtime error: Fannkuch algorithm (array permutation) produces wrong result
    /// Expected: 22
    /// Root Cause: Array manipulation or integer arithmetic issue
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect Fannkuch result - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("access-fannkuch.js")]
    public async Task SunSpider_Access_Fannkuch_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Runtime error: N-body physics simulation produces wrong result
    /// Expected: -1.3524862408537381
    /// Root Cause: Floating-point arithmetic or object property handling
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect N-body result - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("access-nbody.js")]
    public async Task SunSpider_Access_NBody_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    // ====================================================================================
    // FAILING TESTS - RUNTIME ERRORS: 3D GRAPHICS (1)
    // See SUNSPIDER_TEST_FINDINGS.md for detailed analysis
    // ====================================================================================

    /// <summary>
    /// Runtime error: Ray-tracing algorithm produces wrong output length
    /// Root Cause: Complex calculations with arrays and objects, likely floating-point or array handling
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect raytrace output - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("3d-raytrace.js")]
    public async Task SunSpider_3D_Raytrace_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    // ====================================================================================
    // FAILING TESTS - RUNTIME ERRORS: STRING OPERATIONS (2)
    // See SUNSPIDER_TEST_FINDINGS.md for detailed analysis
    // ====================================================================================

    /// <summary>
    /// Runtime error: Base64 encoding produces wrong result
    /// Root Cause: Character/bit manipulation in encoding algorithm
    /// - Proper bit shifting and masking
    /// - Character code operations
    /// - Array indexing
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect base64 encoding - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("string-base64.js")]
    public async Task SunSpider_String_Base64_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    /// <summary>
    /// Runtime error: FASTA string generation produces wrong length
    /// Expected: 1456000
    /// Root Cause: String concatenation or array operations, random number generation
    /// </summary>
    [Theory(Skip = "Runtime error: Incorrect FASTA output - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("string-fasta.js")]
    public async Task SunSpider_String_FASTA_Failing(string filename)
    {
        var content = GetEmbeddedFile(filename);
        await RunTest(content);
    }

    // ====================================================================================
    // FAILING TESTS - RUNTIME ERRORS: DATE FORMATTING (2)
    // See SUNSPIDER_TEST_FINDINGS.md for detailed analysis
    // ====================================================================================

    /// <summary>
    /// Runtime error: Date formatting functions produce incorrect results
    /// Root Cause: Date object methods, string manipulation, or timezone handling
    /// </summary>
    [Theory(Skip = "Runtime error: Date formatting issues - see SUNSPIDER_TEST_FINDINGS.md")]
    [InlineData("date-format-tofte.js")]
    [InlineData("date-format-xparb.js")]
    public async Task SunSpider_Date_Formatting_Failing(string filename)
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

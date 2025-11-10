using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NumberStaticMethodsTests
{
    [Fact]
    public async Task Number_IsInteger_ReturnsTrueForIntegers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isInteger(5);");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Number_IsInteger_ReturnsFalseForDecimals()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isInteger(5.5);");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Number_IsInteger_ReturnsFalseForNaN()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isInteger(0 / 0);");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Number_IsFinite_ReturnsTrueForFiniteNumbers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isFinite(100);");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Number_IsFinite_ReturnsFalseForInfinity()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isFinite(1 / 0);");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Number_IsNaN_ReturnsTrueForNaN()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isNaN(0 / 0);");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Number_IsNaN_ReturnsFalseForNumbers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isNaN(5);");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Number_IsSafeInteger_ReturnsTrueForSafeIntegers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isSafeInteger(100);");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Number_IsSafeInteger_ReturnsFalseForLargeNumbers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.isSafeInteger(9007199254740992);"); // MAX_SAFE_INTEGER + 1
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Number_ParseFloat_ParsesDecimalNumbers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.parseFloat('3.14');");
        Assert.Equal(3.14d, result);
    }

    [Fact]
    public async Task Number_ParseFloat_HandlesLeadingWhitespace()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.parseFloat('  42.5');");
        Assert.Equal(42.5d, result);
    }

    [Fact]
    public async Task Number_ParseFloat_StopsAtNonNumeric()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.parseFloat('3.14abc');");
        Assert.Equal(3.14d, result);
    }

    [Fact]
    public async Task Number_ParseInt_ParsesIntegers()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.parseInt('42');");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task Number_ParseInt_WithRadix()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.parseInt('1010', 2);");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Number_ParseInt_WithHexRadix()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("Number.parseInt('FF', 16);");
        Assert.Equal(255d, result);
    }

    [Fact]
    public async Task Number_Constants_AreAvailable()
    {
        var engine = new JsEngine();
        
        // MAX_SAFE_INTEGER
        var maxSafe = engine.EvaluateSync("Number.MAX_SAFE_INTEGER;");
        Assert.Equal(9007199254740991d, maxSafe);
        
        // MIN_SAFE_INTEGER
        var minSafe = engine.EvaluateSync("Number.MIN_SAFE_INTEGER;");
        Assert.Equal(-9007199254740991d, minSafe);
        
        // POSITIVE_INFINITY
        var posInf = engine.EvaluateSync("Number.POSITIVE_INFINITY;");
        Assert.Equal(double.PositiveInfinity, posInf);
        
        // NEGATIVE_INFINITY
        var negInf = engine.EvaluateSync("Number.NEGATIVE_INFINITY;");
        Assert.Equal(double.NegativeInfinity, negInf);
        
        // NaN
        var nan = engine.EvaluateSync("Number.NaN;");
        Assert.True(double.IsNaN((double)nan!));
    }

    [Fact]
    public async Task Number_ParseFloat_IsCultureInvariant()
    {
        // Save current culture
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        
        try
        {
            // Test with a culture that uses comma as decimal separator (e.g., German)
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            
            var engine = new JsEngine();
            var result = engine.EvaluateSync("Number.parseFloat('3.14');");
            
            // Should parse 3.14 with a dot, not a comma, regardless of culture
            Assert.Equal(3.14d, result);
        }
        finally
        {
            // Restore original culture
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task Number_Constructor_IsCultureInvariant()
    {
        // Save current culture
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        
        try
        {
            // Test with a culture that uses comma as decimal separator (e.g., French)
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            
            var engine = new JsEngine();
            var result = engine.EvaluateSync("Number('42.5');");
            
            // Should parse 42.5 with a dot, not a comma, regardless of culture
            Assert.Equal(42.5d, result);
        }
        finally
        {
            // Restore original culture
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }
}

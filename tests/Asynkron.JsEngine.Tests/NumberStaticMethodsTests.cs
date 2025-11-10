using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NumberStaticMethodsTests
{
    [Fact(Timeout = 2000)]
    public async Task Number_IsInteger_ReturnsTrueForIntegers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isInteger(5);");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsInteger_ReturnsFalseForDecimals()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isInteger(5.5);");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsInteger_ReturnsFalseForNaN()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isInteger(0 / 0);");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsFinite_ReturnsTrueForFiniteNumbers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isFinite(100);");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsFinite_ReturnsFalseForInfinity()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isFinite(1 / 0);");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsNaN_ReturnsTrueForNaN()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isNaN(0 / 0);");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsNaN_ReturnsFalseForNumbers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isNaN(5);");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsSafeInteger_ReturnsTrueForSafeIntegers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isSafeInteger(100);");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_IsSafeInteger_ReturnsFalseForLargeNumbers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.isSafeInteger(9007199254740992);"); // MAX_SAFE_INTEGER + 1
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseFloat_ParsesDecimalNumbers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.parseFloat('3.14');");
        Assert.Equal(3.14d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseFloat_HandlesLeadingWhitespace()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.parseFloat('  42.5');");
        Assert.Equal(42.5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseFloat_StopsAtNonNumeric()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.parseFloat('3.14abc');");
        Assert.Equal(3.14d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseInt_ParsesIntegers()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.parseInt('42');");
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseInt_WithRadix()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.parseInt('1010', 2);");
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseInt_WithHexRadix()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Number.parseInt('FF', 16);");
        Assert.Equal(255d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_Constants_AreAvailable()
    {
        var engine = new JsEngine();
        
        // MAX_SAFE_INTEGER
        var maxSafe = await engine.Evaluate("Number.MAX_SAFE_INTEGER;");
        Assert.Equal(9007199254740991d, maxSafe);
        
        // MIN_SAFE_INTEGER
        var minSafe = await engine.Evaluate("Number.MIN_SAFE_INTEGER;");
        Assert.Equal(-9007199254740991d, minSafe);
        
        // POSITIVE_INFINITY
        var posInf = await engine.Evaluate("Number.POSITIVE_INFINITY;");
        Assert.Equal(double.PositiveInfinity, posInf);
        
        // NEGATIVE_INFINITY
        var negInf = await engine.Evaluate("Number.NEGATIVE_INFINITY;");
        Assert.Equal(double.NegativeInfinity, negInf);
        
        // NaN
        var nan = await engine.Evaluate("Number.NaN;");
        Assert.True(double.IsNaN((double)nan!));
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ParseFloat_IsCultureInvariant()
    {
        // Save current culture
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        
        try
        {
            // Test with a culture that uses comma as decimal separator (e.g., German)
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            
            var engine = new JsEngine();
            var result = await engine.Evaluate("Number.parseFloat('3.14');");
            
            // Should parse 3.14 with a dot, not a comma, regardless of culture
            Assert.Equal(3.14d, result);
        }
        finally
        {
            // Restore original culture
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact(Timeout = 2000)]
    public async Task Number_Constructor_IsCultureInvariant()
    {
        // Save current culture
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        
        try
        {
            // Test with a culture that uses comma as decimal separator (e.g., French)
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            
            var engine = new JsEngine();
            var result = await engine.Evaluate("Number('42.5');");
            
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

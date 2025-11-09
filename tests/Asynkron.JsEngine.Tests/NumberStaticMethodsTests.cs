using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NumberStaticMethodsTests
{
    [Fact]
    public void Number_IsInteger_ReturnsTrueForIntegers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isInteger(5);");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Number_IsInteger_ReturnsFalseForDecimals()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isInteger(5.5);");
        Assert.False((bool)result!);
    }

    [Fact]
    public void Number_IsInteger_ReturnsFalseForNaN()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isInteger(0 / 0);");
        Assert.False((bool)result!);
    }

    [Fact]
    public void Number_IsFinite_ReturnsTrueForFiniteNumbers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isFinite(100);");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Number_IsFinite_ReturnsFalseForInfinity()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isFinite(1 / 0);");
        Assert.False((bool)result!);
    }

    [Fact]
    public void Number_IsNaN_ReturnsTrueForNaN()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isNaN(0 / 0);");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Number_IsNaN_ReturnsFalseForNumbers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isNaN(5);");
        Assert.False((bool)result!);
    }

    [Fact]
    public void Number_IsSafeInteger_ReturnsTrueForSafeIntegers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isSafeInteger(100);");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Number_IsSafeInteger_ReturnsFalseForLargeNumbers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.isSafeInteger(9007199254740992);"); // MAX_SAFE_INTEGER + 1
        Assert.False((bool)result!);
    }

    [Fact]
    public void Number_ParseFloat_ParsesDecimalNumbers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.parseFloat('3.14');");
        Assert.Equal(3.14d, result);
    }

    [Fact]
    public void Number_ParseFloat_HandlesLeadingWhitespace()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.parseFloat('  42.5');");
        Assert.Equal(42.5d, result);
    }

    [Fact]
    public void Number_ParseFloat_StopsAtNonNumeric()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.parseFloat('3.14abc');");
        Assert.Equal(3.14d, result);
    }

    [Fact]
    public void Number_ParseInt_ParsesIntegers()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.parseInt('42');");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void Number_ParseInt_WithRadix()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.parseInt('1010', 2);");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void Number_ParseInt_WithHexRadix()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("Number.parseInt('FF', 16);");
        Assert.Equal(255d, result);
    }

    [Fact]
    public void Number_Constants_AreAvailable()
    {
        var engine = new JsEngine();
        
        // MAX_SAFE_INTEGER
        var maxSafe = engine.Evaluate("Number.MAX_SAFE_INTEGER;");
        Assert.Equal(9007199254740991d, maxSafe);
        
        // MIN_SAFE_INTEGER
        var minSafe = engine.Evaluate("Number.MIN_SAFE_INTEGER;");
        Assert.Equal(-9007199254740991d, minSafe);
        
        // POSITIVE_INFINITY
        var posInf = engine.Evaluate("Number.POSITIVE_INFINITY;");
        Assert.Equal(double.PositiveInfinity, posInf);
        
        // NEGATIVE_INFINITY
        var negInf = engine.Evaluate("Number.NEGATIVE_INFINITY;");
        Assert.Equal(double.NegativeInfinity, negInf);
        
        // NaN
        var nan = engine.Evaluate("Number.NaN;");
        Assert.True(double.IsNaN((double)nan!));
    }
}

using System.Numerics;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Math", ToStringTag = "Math", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class MathPrototype : JsPrototype
{
    [JsHostMethod("abs", Length = 1d)]
    public object Abs(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return double.IsNaN(x) ? double.NaN : Math.Abs(x);
    }

    [JsHostMethod("ceil", Length = 1d)]
    public object Ceil(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Ceiling(x);
    }

    [JsHostMethod("floor", Length = 1d)]
    public object Floor(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Floor(x);
    }

    [JsHostMethod("round", Length = 1d)]
    public object Round(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return x;
        }

        if (x >= 0)
        {
            return Math.Floor(x + 0.5);
        }

        return Math.Ceiling(x - 0.5);
    }

    [JsHostMethod("sqrt", Length = 1d)]
    public object Sqrt(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sqrt(x);
    }

    [JsHostMethod("pow", Length = 2d)]
    public object Pow(object? _, IReadOnlyList<object?> args)
    {
        var baseValue = JsOps.ToNumber(args.GetArgument(0));
        var exponent = JsOps.ToNumber(args.GetArgument(1));
        return JsOps.MathPow(baseValue, exponent);
    }

    [JsHostMethod("max", Length = 2d)]
    public object Max(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return double.NegativeInfinity;
        }

        var max = double.NegativeInfinity;
        foreach (var arg in args)
        {
            var d = JsOps.ToNumber(arg);
            if (double.IsNaN(d))
            {
                return double.NaN;
            }

            if (d > max || double.IsNegativeInfinity(max))
            {
                max = d;
            }
        }

        return max;
    }

    [JsHostMethod("min", Length = 2d)]
    public object Min(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return double.PositiveInfinity;
        }

        var min = double.PositiveInfinity;
        foreach (var arg in args)
        {
            var d = JsOps.ToNumber(arg);
            if (double.IsNaN(d))
            {
                return double.NaN;
            }

            if (d < min || double.IsPositiveInfinity(min))
            {
                min = d;
            }
        }

        return min;
    }

    [JsHostMethod("random", Length = 0d)]
    public object Random(object? _, IReadOnlyList<object?> args)
    {
        return System.Random.Shared.NextDouble();
    }

    [JsHostMethod("sin", Length = 1d)]
    public object Sin(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sin(x);
    }

    [JsHostMethod("cos", Length = 1d)]
    public object Cos(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Cos(x);
    }

    [JsHostMethod("tan", Length = 1d)]
    public object Tan(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Tan(x);
    }

    [JsHostMethod("asin", Length = 1d)]
    public object Asin(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Asin(x);
    }

    [JsHostMethod("acos", Length = 1d)]
    public object Acos(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Acos(x);
    }

    [JsHostMethod("atan", Length = 1d)]
    public object Atan(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Atan(x);
    }

    [JsHostMethod("atan2", Length = 2d)]
    public object Atan2(object? _, IReadOnlyList<object?> args)
    {
        var y = JsOps.ToNumber(args.GetArgument(0));
        var x = JsOps.ToNumber(args.GetArgument(1));
        return Math.Atan2(y, x);
    }

    [JsHostMethod("exp", Length = 1d)]
    public object Exp(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Exp(x);
    }

    [JsHostMethod("log", Length = 1d)]
    public object Log(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log(x);
    }

    [JsHostMethod("log10", Length = 1d)]
    public object Log10(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log10(x);
    }

    [JsHostMethod("log2", Length = 1d)]
    public object Log2(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log2(x);
    }

    [JsHostMethod("trunc", Length = 1d)]
    public object Trunc(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return double.IsNaN(x) || double.IsInfinity(x) ? x : Math.Truncate(x);
    }

    [JsHostMethod("sign", Length = 1d)]
    public object Sign(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        return Math.Sign(x);
    }

    [JsHostMethod("cbrt", Length = 1d)]
    public object Cbrt(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Cbrt(x);
    }

    [JsHostMethod("clz32", Length = 1d)]
    public object Clz32(object? _, IReadOnlyList<object?> args)
    {
        var number = JsOps.ToNumber(args.GetArgument(0));
        var value = JsNumericConversions.ToUInt32(number);
        return value == 0 ? 32d : BitOperations.LeadingZeroCount(value);
    }

    [JsHostMethod("imul", Length = 2d)]
    public object Imul(object? _, IReadOnlyList<object?> args)
    {
        var left = JsOps.ToNumber(args.GetArgument(0));
        var right = JsOps.ToNumber(args.GetArgument(1));
        var a = JsNumericConversions.ToInt32(left);
        var b = JsNumericConversions.ToInt32(right);
        return (double)(a * b);
    }

    [JsHostMethod("fround", Length = 1d)]
    public object Fround(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return (double)(float)x;
    }

    [JsHostMethod("hypot", Length = 2d)]
    public object Hypot(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return 0d;
        }

        var hasInfinity = false;
        var hasNaN = false;
        double sumOfSquares = 0;
        foreach (var arg in args)
        {
            var number = JsOps.ToNumber(arg);
            if (double.IsInfinity(number))
            {
                hasInfinity = true;
                continue;
            }

            if (double.IsNaN(number))
            {
                hasNaN = true;
                continue;
            }

            sumOfSquares += number * number;
        }

        if (hasInfinity)
        {
            return double.PositiveInfinity;
        }

        return hasNaN ? double.NaN : Math.Sqrt(sumOfSquares);
    }

    [JsHostMethod("acosh", Length = 1d)]
    public object Acosh(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Acosh(x);
    }

    [JsHostMethod("asinh", Length = 1d)]
    public object Asinh(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Asinh(x);
    }

    [JsHostMethod("atanh", Length = 1d)]
    public object Atanh(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Atanh(x);
    }

    [JsHostMethod("cosh", Length = 1d)]
    public object Cosh(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Cosh(x);
    }

    [JsHostMethod("sinh", Length = 1d)]
    public object Sinh(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sinh(x);
    }

    [JsHostMethod("tanh", Length = 1d)]
    public object Tanh(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Tanh(x);
    }

    [JsHostMethod("expm1", Length = 1d)]
    public object Expm1(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Exp(x) - 1;
    }

    [JsHostMethod("log1p", Length = 1d)]
    public object Log1p(object? _, IReadOnlyList<object?> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log(1 + x);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        DefineConstantProperty(Prototype, "E", Math.E);
        DefineConstantProperty(Prototype, "PI", Math.PI);
        DefineConstantProperty(Prototype, "LN2", Math.Log(2));
        DefineConstantProperty(Prototype, "LN10", Math.Log(10));
        DefineConstantProperty(Prototype, "LOG2E", Math.Log2(Math.E));
        DefineConstantProperty(Prototype, "LOG10E", Math.Log10(Math.E));
        DefineConstantProperty(Prototype, "SQRT1_2", Math.Sqrt(0.5));
        DefineConstantProperty(Prototype, "SQRT2", Math.Sqrt(2));
    }
}

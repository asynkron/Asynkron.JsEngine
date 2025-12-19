using System.Numerics;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Math", ToStringTag = "Math", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class MathPrototype
{
    [JsHostMethod("abs", Length = 1d)]
    public JsValue Abs(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return double.IsNaN(x) ? double.NaN : Math.Abs(x);
    }

    [JsHostMethod("ceil", Length = 1d)]
    public JsValue Ceil(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Ceiling(x);
    }

    [JsHostMethod("floor", Length = 1d)]
    public JsValue Floor(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Floor(x);
    }

    [JsHostMethod("round", Length = 1d)]
    public JsValue Round(IReadOnlyList<JsValue> args)
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
    public JsValue Sqrt(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sqrt(x);
    }

    [JsHostMethod("pow", Length = 2d)]
    public JsValue Pow(IReadOnlyList<JsValue> args)
    {
        var baseValue = JsOps.ToNumber(args.GetArgument(0));
        var exponent = JsOps.ToNumber(args.GetArgument(1));
        return JsOps.MathPow(baseValue, exponent);
    }

    [JsHostMethod("max", Length = 2d)]
    public JsValue Max(IReadOnlyList<JsValue> args)
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
    public JsValue Min(IReadOnlyList<JsValue> args)
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
    public JsValue Random(JsValue thisValue)
    {
        return System.Random.Shared.NextDouble();
    }

    [JsHostMethod("sin", Length = 1d)]
    public JsValue Sin(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sin(x);
    }

    [JsHostMethod("cos", Length = 1d)]
    public JsValue Cos(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Cos(x);
    }

    [JsHostMethod("tan", Length = 1d)]
    public JsValue Tan(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Tan(x);
    }

    [JsHostMethod("asin", Length = 1d)]
    public JsValue Asin(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Asin(x);
    }

    [JsHostMethod("acos", Length = 1d)]
    public JsValue Acos(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Acos(x);
    }

    [JsHostMethod("atan", Length = 1d)]
    public JsValue Atan(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Atan(x);
    }

    [JsHostMethod("atan2", Length = 2d)]
    public JsValue Atan2(IReadOnlyList<JsValue> args)
    {
        var y = JsOps.ToNumber(args.GetArgument(0));
        var x = JsOps.ToNumber(args.GetArgument(1));
        return Math.Atan2(y, x);
    }

    [JsHostMethod("exp", Length = 1d)]
    public JsValue Exp(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Exp(x);
    }

    [JsHostMethod("log", Length = 1d)]
    public JsValue Log(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log(x);
    }

    [JsHostMethod("log10", Length = 1d)]
    public JsValue Log10(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log10(x);
    }

    [JsHostMethod("log2", Length = 1d)]
    public JsValue Log2(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log2(x);
    }

    [JsHostMethod("trunc", Length = 1d)]
    public JsValue Trunc(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return double.IsNaN(x) || double.IsInfinity(x) ? x : Math.Truncate(x);
    }

    [JsHostMethod("sign", Length = 1d)]
    public JsValue Sign(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        return Math.Sign(x);
    }

    [JsHostMethod("cbrt", Length = 1d)]
    public JsValue Cbrt(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Cbrt(x);
    }

    [JsHostMethod("clz32", Length = 1d)]
    public JsValue Clz32(IReadOnlyList<JsValue> args)
    {
        var number = JsOps.ToNumber(args.GetArgument(0));
        var value = JsNumericConversions.ToUInt32(number);
        return value == 0 ? 32d : BitOperations.LeadingZeroCount(value);
    }

    [JsHostMethod("imul", Length = 2d)]
    public JsValue Imul(IReadOnlyList<JsValue> args)
    {
        var left = JsOps.ToNumber(args.GetArgument(0));
        var right = JsOps.ToNumber(args.GetArgument(1));
        var a = JsNumericConversions.ToInt32(left);
        var b = JsNumericConversions.ToInt32(right);
        return (double)(a * b);
    }

    [JsHostMethod("fround", Length = 1d)]
    public JsValue Fround(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return (double)(float)x;
    }

    [JsHostMethod("hypot", Length = 2d)]
    public JsValue Hypot(IReadOnlyList<JsValue> args)
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
    public JsValue Acosh(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Acosh(x);
    }

    [JsHostMethod("asinh", Length = 1d)]
    public JsValue Asinh(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Asinh(x);
    }

    [JsHostMethod("atanh", Length = 1d)]
    public JsValue Atanh(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Atanh(x);
    }

    [JsHostMethod("cosh", Length = 1d)]
    public JsValue Cosh(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Cosh(x);
    }

    [JsHostMethod("sinh", Length = 1d)]
    public JsValue Sinh(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sinh(x);
    }

    [JsHostMethod("tanh", Length = 1d)]
    public JsValue Tanh(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Tanh(x);
    }

    [JsHostMethod("expm1", Length = 1d)]
    public JsValue Expm1(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Exp(x) - 1;
    }

    [JsHostMethod("log1p", Length = 1d)]
    public JsValue Log1p(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Log(1 + x);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
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

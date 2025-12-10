using System.Collections.Generic;
using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates a Math object with common mathematical functions and constants.
    /// </summary>
    public static JsObject CreateMathObject(RealmState? realm = null)
    {
        var math = new JsObject();
        if (realm?.ObjectPrototype is not null)
        {
            math.SetPrototype(realm.ObjectPrototype);
        }

        HostFunction Fn(Func<IReadOnlyList<object?>, object?> impl) =>
            new HostFunction(impl, realm, isConstructor: false);

        var toStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        math.DefineProperty(toStringTagKey,
            new PropertyDescriptor { Value = "Math", Writable = false, Enumerable = false, Configurable = true });

        // Constants
        DefineConstantProperty(math, "E", Math.E);
        DefineConstantProperty(math, "PI", Math.PI);
        DefineConstantProperty(math, "LN2", Math.Log(2));
        DefineConstantProperty(math, "LN10", Math.Log(10));
        DefineConstantProperty(math, "LOG2E", Math.Log2(Math.E));
        DefineConstantProperty(math, "LOG10E", Math.Log10(Math.E));
        DefineConstantProperty(math, "SQRT1_2", Math.Sqrt(0.5));
        DefineConstantProperty(math, "SQRT2", Math.Sqrt(2));

        // Methods
        math["abs"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] switch
            {
                double d => Math.Abs(d),
                int i => Math.Abs(i),
                _ => double.NaN
            };
        });

        math["ceil"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Ceiling(d) : double.NaN;
        });

        math["floor"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Floor(d) : double.NaN;
        });

        math["round"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            if (args[0] is not double d)
            {
                return double.NaN;
            }

            // JavaScript Math.round uses "round half away from zero"
            // while .NET Math.Round uses "round half to even" by default
            if (d >= 0)
            {
                return Math.Floor(d + 0.5);
            }

            return Math.Ceiling(d - 0.5);
        });

        math["sqrt"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sqrt(d) : double.NaN;
        });

        math["pow"] = Fn(args =>
        {
            if (args.Count < 2)
            {
                return double.NaN;
            }

            var baseValue = args[0] as double? ?? double.NaN;
            var exponent = args[1] as double? ?? double.NaN;
            return JsOps.MathPow(baseValue, exponent);
        });

        math["max"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NegativeInfinity;
            }

            var max = double.NegativeInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d))
                    {
                        return double.NaN;
                    }

                    if (d > max)
                    {
                        max = d;
                    }
                }
            }

            return max;
        });

        math["min"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.PositiveInfinity;
            }

            var min = double.PositiveInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d))
                    {
                        return double.NaN;
                    }

                    if (d < min)
                    {
                        min = d;
                    }
                }
            }

            return min;
        });

        math["random"] = Fn(_ => Random.Shared.NextDouble());

        math["sin"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sin(d) : double.NaN;
        });

        math["cos"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cos(d) : double.NaN;
        });

        math["tan"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Tan(d) : double.NaN;
        });

        math["asin"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Asin(d) : double.NaN;
        });

        math["acos"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Acos(d) : double.NaN;
        });

        math["atan"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Atan(d) : double.NaN;
        });

        math["atan2"] = Fn(args =>
        {
            if (args.Count < 2)
            {
                return double.NaN;
            }

            var y = args[0] as double? ?? double.NaN;
            var x = args[1] as double? ?? double.NaN;
            return Math.Atan2(y, x);
        });

        math["exp"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Exp(d) : double.NaN;
        });

        math["log"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log(d) : double.NaN;
        });

        math["log10"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log10(d) : double.NaN;
        });

        math["log2"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log2(d) : double.NaN;
        });

        math["trunc"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Truncate(d) : double.NaN;
        });

        math["sign"] = Fn(args =>
        {
            if (args.Count == 0 || args[0] is not double d || double.IsNaN(d))
            {
                return double.NaN;
            }

            return Math.Sign(d);
        });

        // ES6+ Math methods
        math["cbrt"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cbrt(d) : double.NaN;
        });

        math["clz32"] = Fn(args =>
        {
            var number = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
            var value = JsNumericConversions.ToUInt32(number);
            if (value == 0)
            {
                return 32d;
            }

            return (double)BitOperations.LeadingZeroCount(value);
        });

        math["imul"] = Fn(args =>
        {
            var left = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
            var right = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0d;
            var a = JsNumericConversions.ToInt32(left);
            var b = JsNumericConversions.ToInt32(right);
            return (double)(a * b);
        });

        math["fround"] = Fn(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return double.NaN;
            }

            return (double)(float)d;
        });

        var hypot = Fn(args =>
        {
            if (args.Count == 0)
            {
                return 0d;
            }

            var coerced = new List<double>(args.Count);
            foreach (var arg in args)
            {
                coerced.Add(JsOps.ToNumber(arg));
            }

            var hasInfinity = false;
            var hasNaN = false;
            double sumOfSquares = 0;
            foreach (var number in coerced)
            {
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
        });
        hypot.Properties.DeleteOwnProperty("prototype");
        hypot.DefineProperty("name",
            new PropertyDescriptor { Value = "hypot", Writable = false, Enumerable = false, Configurable = true });
        hypot.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });
        math.DefineProperty("hypot",
            new PropertyDescriptor { Value = hypot, Writable = true, Enumerable = false, Configurable = true });

        math["acosh"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Acosh(d) : double.NaN;
        });

        math["asinh"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Asinh(d) : double.NaN;
        });

        math["atanh"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Atanh(d) : double.NaN;
        });

        math["cosh"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cosh(d) : double.NaN;
        });

        math["sinh"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sinh(d) : double.NaN;
        });

        math["tanh"] = Fn(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Tanh(d) : double.NaN;
        });

        math["expm1"] = Fn(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return double.NaN;
            }

            // e^x - 1 with better precision for small x
            return Math.Exp(d) - 1;
        });

        math["log1p"] = Fn(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return double.NaN;
            }

            // log(1 + x) with better precision for small x
            return Math.Log(1 + d);
        });

        foreach (var entry in math)
        {
            if (entry.Value is HostFunction hostFunction)
            {
                hostFunction.IsConstructor = false;
            }
        }

        return math;
    }
}

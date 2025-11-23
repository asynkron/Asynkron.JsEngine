using System.Numerics;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates a Math object with common mathematical functions and constants.
    /// </summary>
    public static JsObject CreateMathObject()
    {
        var math = new JsObject();

        // Constants
        math["E"] = Math.E;
        math["PI"] = Math.PI;
        math["LN2"] = Math.Log(2);
        math["LN10"] = Math.Log(10);
        math["LOG2E"] = Math.Log2(Math.E);
        math["LOG10E"] = Math.Log10(Math.E);
        math["SQRT1_2"] = Math.Sqrt(0.5);
        math["SQRT2"] = Math.Sqrt(2);

        // Methods
        math["abs"] = new HostFunction(args =>
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

        math["ceil"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Ceiling(d) : double.NaN;
        });

        math["floor"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Floor(d) : double.NaN;
        });

        math["round"] = new HostFunction(args =>
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

        math["sqrt"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sqrt(d) : double.NaN;
        });

        math["pow"] = new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return double.NaN;
            }

            var baseValue = args[0] as double? ?? double.NaN;
            var exponent = args[1] as double? ?? double.NaN;
            return Math.Pow(baseValue, exponent);
        });

        math["max"] = new HostFunction(args =>
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

        math["min"] = new HostFunction(args =>
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

        math["random"] = new HostFunction(_ => Random.Shared.NextDouble());

        math["sin"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sin(d) : double.NaN;
        });

        math["cos"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cos(d) : double.NaN;
        });

        math["tan"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Tan(d) : double.NaN;
        });

        math["asin"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Asin(d) : double.NaN;
        });

        math["acos"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Acos(d) : double.NaN;
        });

        math["atan"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Atan(d) : double.NaN;
        });

        math["atan2"] = new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return double.NaN;
            }

            var y = args[0] as double? ?? double.NaN;
            var x = args[1] as double? ?? double.NaN;
            return Math.Atan2(y, x);
        });

        math["exp"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Exp(d) : double.NaN;
        });

        math["log"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log(d) : double.NaN;
        });

        math["log10"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log10(d) : double.NaN;
        });

        math["log2"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log2(d) : double.NaN;
        });

        math["trunc"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Truncate(d) : double.NaN;
        });

        math["sign"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d || double.IsNaN(d))
            {
                return double.NaN;
            }

            return Math.Sign(d);
        });

        // ES6+ Math methods
        math["cbrt"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cbrt(d) : double.NaN;
        });

        math["clz32"] = new HostFunction(args =>
        {
            var number = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
            var value = JsNumericConversions.ToUInt32(number);
            if (value == 0)
            {
                return 32d;
            }

            return (double)BitOperations.LeadingZeroCount(value);
        });

        math["imul"] = new HostFunction(args =>
        {
            var left = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
            var right = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0d;
            var a = JsNumericConversions.ToInt32(left);
            var b = JsNumericConversions.ToInt32(right);
            return (double)(a * b);
        });

        math["fround"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return double.NaN;
            }

            return (double)(float)d;
        });

        var hypot = new HostFunction(args =>
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
        }) { IsConstructor = false };
        hypot.Properties.DeleteOwnProperty("prototype");
        hypot.DefineProperty("name",
            new PropertyDescriptor { Value = "hypot", Writable = false, Enumerable = false, Configurable = true });
        hypot.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });
        math.DefineProperty("hypot",
            new PropertyDescriptor { Value = hypot, Writable = true, Enumerable = false, Configurable = true });

        math["acosh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Acosh(d) : double.NaN;
        });

        math["asinh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Asinh(d) : double.NaN;
        });

        math["atanh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Atanh(d) : double.NaN;
        });

        math["cosh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cosh(d) : double.NaN;
        });

        math["sinh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sinh(d) : double.NaN;
        });

        math["tanh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Tanh(d) : double.NaN;
        });

        math["expm1"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return double.NaN;
            }

            // e^x - 1 with better precision for small x
            return Math.Exp(d) - 1;
        });

        math["log1p"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return double.NaN;
            }

            // log(1 + x) with better precision for small x
            return Math.Log(1 + d);
        });

        return math;
    }
}

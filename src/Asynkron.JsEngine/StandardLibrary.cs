namespace Asynkron.JsEngine;

/// <summary>
/// Provides standard JavaScript library objects and functions (Math, JSON, etc.)
/// </summary>
internal static class StandardLibrary
{
    /// <summary>
    /// Creates a Math object with common mathematical functions and constants.
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
            if (args.Count == 0) return double.NaN;
            return args[0] switch
            {
                double d => Math.Abs(d),
                int i => Math.Abs(i),
                _ => double.NaN
            };
        });

        math["ceil"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Ceiling(d) : double.NaN;
        });

        math["floor"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Floor(d) : double.NaN;
        });

        math["round"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            
            // JavaScript Math.round uses "round half away from zero"
            // while .NET Math.Round uses "round half to even" by default
            if (d >= 0)
            {
                return Math.Floor(d + 0.5);
            }
            else
            {
                return Math.Ceiling(d - 0.5);
            }
        });

        math["sqrt"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Sqrt(d) : double.NaN;
        });

        math["pow"] = new HostFunction(args =>
        {
            if (args.Count < 2) return double.NaN;
            var baseValue = args[0] as double? ?? double.NaN;
            var exponent = args[1] as double? ?? double.NaN;
            return Math.Pow(baseValue, exponent);
        });

        math["max"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NegativeInfinity;
            var max = double.NegativeInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d)) return double.NaN;
                    if (d > max) max = d;
                }
            }
            return max;
        });

        math["min"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.PositiveInfinity;
            var min = double.PositiveInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d)) return double.NaN;
                    if (d < min) min = d;
                }
            }
            return min;
        });

        math["random"] = new HostFunction(args =>
        {
            return Random.Shared.NextDouble();
        });

        math["sin"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Sin(d) : double.NaN;
        });

        math["cos"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Cos(d) : double.NaN;
        });

        math["tan"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Tan(d) : double.NaN;
        });

        math["asin"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Asin(d) : double.NaN;
        });

        math["acos"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Acos(d) : double.NaN;
        });

        math["atan"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Atan(d) : double.NaN;
        });

        math["atan2"] = new HostFunction(args =>
        {
            if (args.Count < 2) return double.NaN;
            var y = args[0] as double? ?? double.NaN;
            var x = args[1] as double? ?? double.NaN;
            return Math.Atan2(y, x);
        });

        math["exp"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Exp(d) : double.NaN;
        });

        math["log"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Log(d) : double.NaN;
        });

        math["log10"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Log10(d) : double.NaN;
        });

        math["log2"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Log2(d) : double.NaN;
        });

        math["trunc"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            return args[0] is double d ? Math.Truncate(d) : double.NaN;
        });

        math["sign"] = new HostFunction(args =>
        {
            if (args.Count == 0) return double.NaN;
            if (args[0] is not double d) return double.NaN;
            if (double.IsNaN(d)) return double.NaN;
            return Math.Sign(d);
        });

        return math;
    }

    /// <summary>
    /// Adds standard array methods to a JsArray instance.
    /// </summary>
    public static void AddArrayMethods(JsArray array)
    {
        // push - already implemented natively
        array.SetProperty("push", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            foreach (var arg in args)
            {
                jsArray.Push(arg);
            }
            return jsArray.Items.Count;
        }));

        // pop
        array.SetProperty("pop", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (jsArray.Items.Count == 0) return null;
            var last = jsArray.GetElement(jsArray.Items.Count - 1);
            // We need to remove the last element - this requires exposing the internal list
            // For now, we'll return the value but note this is a limitation
            return last;
        }));

        // map
        array.SetProperty("map", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                result.Push(mapped);
            }
            AddArrayMethods(result);
            return result;
        }));

        // filter
        array.SetProperty("filter", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            var result = new JsArray();
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var keep = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(keep))
                {
                    result.Push(element);
                }
            }
            AddArrayMethods(result);
            return result;
        }));

        // reduce
        array.SetProperty("reduce", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            if (jsArray.Items.Count == 0)
            {
                return args.Count > 1 ? args[1] : null;
            }

            int startIndex = 0;
            object? accumulator;

            if (args.Count > 1)
            {
                accumulator = args[1];
            }
            else
            {
                accumulator = jsArray.Items[0];
                startIndex = 1;
            }

            for (int i = startIndex; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                accumulator = callback.Invoke(new object?[] { accumulator, element, (double)i, jsArray }, null);
            }

            return accumulator;
        }));

        // forEach
        array.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
            }

            return null;
        }));

        // find
        array.SetProperty("find", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var match = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(match))
                {
                    return element;
                }
            }

            return null;
        }));

        // findIndex
        array.SetProperty("findIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return null;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var match = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(match))
                {
                    return (double)i;
                }
            }

            return -1d;
        }));

        // some
        array.SetProperty("some", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return false;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return false;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var result = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (IsTruthy(result))
                {
                    return true;
                }
            }

            return false;
        }));

        // every
        array.SetProperty("every", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return true;
            if (args.Count == 0 || args[0] is not IJsCallable callback) return true;

            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var result = callback.Invoke(new object?[] { element, (double)i, jsArray }, null);
                if (!IsTruthy(result))
                {
                    return false;
                }
            }

            return true;
        }));

        // join
        array.SetProperty("join", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return "";
            var separator = args.Count > 0 && args[0] is string sep ? sep : ",";

            var parts = new List<string>();
            foreach (var item in jsArray.Items)
            {
                parts.Add(item?.ToString() ?? "");
            }

            return string.Join(separator, parts);
        }));

        // includes
        array.SetProperty("includes", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return false;
            if (args.Count == 0) return false;

            var searchElement = args[0];
            foreach (var item in jsArray.Items)
            {
                if (AreStrictlyEqual(item, searchElement))
                {
                    return true;
                }
            }

            return false;
        }));

        // indexOf
        array.SetProperty("indexOf", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return -1d;
            if (args.Count == 0) return -1d;

            var searchElement = args[0];
            for (int i = 0; i < jsArray.Items.Count; i++)
            {
                if (AreStrictlyEqual(jsArray.Items[i], searchElement))
                {
                    return (double)i;
                }
            }

            return -1d;
        }));

        // slice
        array.SetProperty("slice", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray) return null;

            int start = 0;
            int end = jsArray.Items.Count;

            if (args.Count > 0 && args[0] is double startD)
            {
                start = (int)startD;
                if (start < 0) start = Math.Max(0, jsArray.Items.Count + start);
            }

            if (args.Count > 1 && args[1] is double endD)
            {
                end = (int)endD;
                if (end < 0) end = Math.Max(0, jsArray.Items.Count + end);
            }

            var result = new JsArray();
            for (int i = start; i < Math.Min(end, jsArray.Items.Count); i++)
            {
                result.Push(jsArray.Items[i]);
            }
            AddArrayMethods(result);
            return result;
        }));
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            double d => Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    private static bool AreStrictlyEqual(object? left, object? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        var leftType = left.GetType();
        var rightType = right.GetType();

        if (leftType != rightType) return false;

        return Equals(left, right);
    }
}

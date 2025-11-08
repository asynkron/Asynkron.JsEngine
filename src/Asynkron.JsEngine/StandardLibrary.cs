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
}

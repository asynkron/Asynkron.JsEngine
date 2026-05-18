#region

using System.Numerics;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Math", ToStringTag = "Math", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class MathPrototype
{
    [JsHostMethod("abs", Length = 1d)]
    public static JsValue Abs(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        return x == 0 ? 0d : Math.Abs(x);
    }

    [JsHostMethod("ceil", Length = 1d)]
    public static JsValue Ceil(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Ceiling(x);
    }

    [JsHostMethod("floor", Length = 1d)]
    public static JsValue Floor(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Floor(x);
    }

    [JsHostMethod("round", Length = 1d)]
    public static JsValue Round(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));

        // Handle special values
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return x;
        }

        // Preserve signed zeros: round(-0) = -0, round(+0) = +0
        if (x == 0)
        {
            return x;
        }

        // Per spec: If x is greater than 0 but less than 0.5, the result is +0
        // If x is less than 0 but greater than or equal to -0.5, the result is -0
        if (x > 0 && x < 0.5)
        {
            return 0d;
        }

        if (x < 0 && x >= -0.5)
        {
            return -0d;
        }

        // For large magnitudes where x is already an integer (|x| >= 2^52),
        // return x directly to avoid precision loss when adding 0.5
        // IEEE 754 double precision can only represent integers exactly up to 2^53
        const double maxSafeForRounding = 4503599627370496d; // 2^52
        if (x >= maxSafeForRounding || x <= -maxSafeForRounding)
        {
            return x;
        }

        // Use floor(x) and check fractional part to avoid precision issues with x + 0.5
        var floored = Math.Floor(x);
        var frac = x - floored;

        // Round half away from zero (toward +infinity for ties)
        // frac >= 0.5 means round up
        if (frac >= 0.5)
        {
            return floored + 1;
        }

        return floored;
    }

    [JsHostMethod("sqrt", Length = 1d)]
    public static JsValue Sqrt(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sqrt(x);
    }

    [JsHostMethod("pow", Length = 2d)]
    public static JsValue Pow(IReadOnlyList<JsValue> args)
    {
        var baseValue = JsOps.ToNumber(args.GetArgument(0));
        var exponent = JsOps.ToNumber(args.GetArgument(1));
        return JsOps.MathPow(baseValue, exponent);
    }

    [JsHostMethod("max", Length = 2d)]
    public static JsValue Max(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return double.NegativeInfinity;
        }

        // Must coerce all arguments first (for side effects like valueOf)
        var coerced = new double[args.Count];
        var hasNaN = false;
        for (var i = 0; i < args.Count; i++)
        {
            var d = JsOps.ToNumber(args[i]);
            coerced[i] = d;
            if (double.IsNaN(d))
            {
                hasNaN = true;
            }
        }

        if (hasNaN)
        {
            return double.NaN;
        }

        var max = double.NegativeInfinity;
        foreach (var d in coerced)
        {
            // Per spec: +0 is considered to be larger than -0
            if (d > max || (d == 0 && max == 0 && double.IsNegative(max) && !double.IsNegative(d)))
            {
                max = d;
            }
        }

        return max;
    }

    [JsHostMethod("min", Length = 2d)]
    public static JsValue Min(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return double.PositiveInfinity;
        }

        // Must coerce all arguments first (for side effects like valueOf)
        var coerced = new double[args.Count];
        var hasNaN = false;
        for (var i = 0; i < args.Count; i++)
        {
            var d = JsOps.ToNumber(args[i]);
            coerced[i] = d;
            if (double.IsNaN(d))
            {
                hasNaN = true;
            }
        }

        if (hasNaN)
        {
            return double.NaN;
        }

        var min = double.PositiveInfinity;
        foreach (var d in coerced)
        {
            // Per spec: +0 is considered to be larger than -0, so -0 is the minimum
            if (d < min || (d == 0 && min == 0 && !double.IsNegative(min) && double.IsNegative(d)))
            {
                min = d;
            }
        }

        return min;
    }

    [JsHostMethod("random", Length = 0d)]
    public static JsValue Random()
    {
        return System.Random.Shared.NextDouble();
    }

    [JsHostMethod("sin", Length = 1d)]
    public static JsValue Sin(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));
        return Math.Sin(x);
    }

    [JsHostMethod("cos", Length = 1d)]
    public static JsValue Cos(IReadOnlyList<JsValue> args)
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

        if (IsNegativeZero(y) && x == 0d && !IsNegativeZero(x))
        {
            return JsValue.FromDouble(-0d);
        }

        return Math.Atan2(y, x);
    }

    private static bool IsNegativeZero(double value)
    {
        return value == 0d && BitConverter.DoubleToInt64Bits(value) < 0;
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

        // Preserve signed zeros: sign(+0) = +0, sign(-0) = -0
        if (x == 0)
        {
            return x; // Preserves sign of zero
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

        // Handle special values per spec
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        // Preserve signed zeros: expm1(+0) = +0, expm1(-0) = -0
        if (x == 0)
        {
            return x; // Preserves sign of zero
        }

        if (double.IsNegativeInfinity(x))
        {
            return -1d;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        return Math.Exp(x) - 1;
    }

    [JsHostMethod("log1p", Length = 1d)]
    public JsValue Log1p(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));

        // Handle special values per spec
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        // log1p(x) for x < -1 returns NaN
        if (x < -1)
        {
            return double.NaN;
        }

        // log1p(-1) = -Infinity
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (x == -1)
        {
            return double.NegativeInfinity;
        }

        // Preserve signed zeros: log1p(+0) = +0, log1p(-0) = -0
        if (x == 0)
        {
            return x; // Preserves sign of zero
        }

        // log1p(+Infinity) = +Infinity
        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        return Math.Log(1 + x);
    }

    [JsHostMethod("f16round", Length = 1d)]
    public JsValue F16round(IReadOnlyList<JsValue> args)
    {
        var x = JsOps.ToNumber(args.GetArgument(0));

        // Handle special values
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            return x;
        }

        // Convert to Half (float16) and back to double
        // This performs the rounding to float16 precision
        var half = (Half)x;
        return (double)half;
    }

    /// <summary>
    /// Math.sumPrecise(items) — Returns the sum of the elements of items using
    /// Shewchuk's exact floating-point summation algorithm (Python fsum style).
    /// </summary>
    [JsHostMethod("sumPrecise", Length = 1d)]
    public JsValue SumPrecise(IReadOnlyList<JsValue> args)
    {
        var items = args.GetArgument(0);

        var hasNaN = false;
        var posInf = false;
        var negInf = false;
        var sawFiniteNonZero = false;
        var sawNegativeZero = false;
        var sawPositiveZero = false;

        // Collect all finite values for batch summation
        var values = new List<double>();

        MapSetIterationHelper.Iterate(items, Realm, "Math.sumPrecise", value =>
        {
            if (!value.IsNumber)
            {
                throw ThrowTypeError("Math.sumPrecise requires Number values", realm: Realm);
            }

            var n = value.NumberValue;

            if (double.IsNaN(n))
            {
                hasNaN = true;
            }
            else if (double.IsPositiveInfinity(n))
            {
                posInf = true;
            }
            else if (double.IsNegativeInfinity(n))
            {
                negInf = true;
            }
            else
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (n == 0d)
                {
                    if (double.IsNegative(n))
                    {
                        sawNegativeZero = true;
                    }
                    else
                    {
                        sawPositiveZero = true;
                    }
                }
                else
                {
                    sawFiniteNonZero = true;
                }

                values.Add(n);
            }
        });

        // Per spec: NaN wins, then conflicting infinities = NaN
        if (hasNaN) return JsValue.NaN;
        if (posInf && negInf) return JsValue.NaN;
        if (posInf) return JsValue.PositiveInfinity;
        if (negInf) return JsValue.NegativeInfinity;

        if (values.Count == 0)
        {
            if (!sawFiniteNonZero && sawNegativeZero && !sawPositiveZero) return JsValue.FromDouble(-0d);
            return JsValue.Zero;
        }

        var result = SumPreciseFinite(values);

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (result == 0d && !sawFiniteNonZero && sawNegativeZero && !sawPositiveZero) return JsValue.FromDouble(-0d);

        return JsValue.FromDouble(result);
    }

    /// <summary>
    /// Exact summation of finite doubles using BigInteger exact arithmetic.
    /// Each double is decomposed into mantissa * 2^exponent, summed exactly,
    /// then rounded back to double with correct IEEE 754 rounding.
    /// </summary>
    private static double SumPreciseFinite(List<double> values)
    {
        // Accumulate exact sum as BigInteger * 2^shift
        var exactSum = System.Numerics.BigInteger.Zero;
        var minExp = 0;

        // First pass: find minimum exponent to align all values
        foreach (var v in values)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (v == 0.0) continue;
            var bits = BitConverter.DoubleToInt64Bits(v);
            var biasedExp = (int)((bits >> 52) & 0x7FF);
            var exp = biasedExp == 0
                ? -1074 // subnormal: exponent is fixed at -1074
                : biasedExp - 1023 - 52;
            if (exp < minExp) minExp = exp;
        }

        // Second pass: accumulate exact integer sum (all values scaled to minExp)
        foreach (var v in values)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (v == 0.0) continue;
            var bits = BitConverter.DoubleToInt64Bits(v);
            var sign = (bits >> 63) != 0;
            var biasedExp = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & 0x000FFFFFFFFFFFFFL;

            int exp;
            if (biasedExp == 0)
            {
                // Subnormal
                exp = -1074;
            }
            else
            {
                // Normal: add implicit 1 bit
                mantissa |= 0x0010000000000000L;
                exp = biasedExp - 1023 - 52;
            }

            var bigMantissa = new System.Numerics.BigInteger(mantissa);
            if (sign) bigMantissa = -bigMantissa;

            // Shift to align with minExp
            var shift = exp - minExp;
            if (shift > 0) bigMantissa <<= shift;

            exactSum += bigMantissa;
        }

        if (exactSum.IsZero) return 0.0;

        // Convert BigInteger back to double with correct rounding
        return BigIntegerToDouble(exactSum, minExp);
    }

    /// <summary>
    /// Converts exactSum * 2^baseExp to a correctly-rounded double.
    /// </summary>
    private static double BigIntegerToDouble(System.Numerics.BigInteger value, int baseExp)
    {
        if (value.IsZero) return 0.0;

        var negative = value < 0;
        if (negative) value = -value;

        // Find the position of the highest set bit
        var bitLength = (int)value.GetBitLength();

        // The double mantissa has 53 bits (including implicit 1)
        // We need to extract bits [bitLength-1 .. bitLength-53] and apply rounding

        // Target exponent for the result
        var resultExp = baseExp + bitLength - 1;

        // Check for overflow
        if (resultExp > 1023)
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        // Check for underflow (subnormal or zero)
        if (resultExp < -1074)
        {
            return negative ? -0.0 : 0.0;
        }

        long mantissaBits;
        if (resultExp >= -1022)
        {
            // Normal number: we need 53 bits of mantissa
            var shiftRight = bitLength - 53;
            if (shiftRight > 0)
            {
                // Round: check the bits we're discarding
                var discarded = value & ((System.Numerics.BigInteger.One << shiftRight) - 1);
                var halfway = System.Numerics.BigInteger.One << (shiftRight - 1);
                value >>= shiftRight;

                // Round to nearest, ties to even
                if (discarded > halfway || (discarded == halfway && (value & 1) == 1))
                {
                    value += 1;
                    // Check if rounding caused carry into the next bit
                    if (value.GetBitLength() > 53)
                    {
                        value >>= 1;
                        resultExp++;
                        if (resultExp > 1023)
                        {
                            return negative ? double.NegativeInfinity : double.PositiveInfinity;
                        }
                    }
                }
            }
            else if (shiftRight < 0)
            {
                value <<= -shiftRight;
            }

            mantissaBits = (long)(value & 0x000FFFFFFFFFFFFFL); // remove implicit 1
            var biasedExp = resultExp + 1023;
            var doubleBits = ((long)biasedExp << 52) | mantissaBits;
            if (negative) doubleBits |= unchecked((long)0x8000000000000000L);
            return BitConverter.Int64BitsToDouble(doubleBits);
        }
        else
        {
            // Subnormal: resultExp < -1022
            // Mantissa bits = value shifted so that the lowest bit represents 2^(-1074)
            var targetBitPos = -1074 - baseExp; // bit position 0 corresponds to 2^baseExp
            var shiftRight = targetBitPos > 0 ? 0 : -targetBitPos;
            // Actually: we need the mantissa bits such that value * 2^baseExp = mantissa * 2^(-1074)
            // mantissa = value * 2^(baseExp + 1074) = value >> -(baseExp + 1074) if baseExp + 1074 < 0
            var totalShift = -(baseExp + 1074); // should be >= 0 for subnormals
            if (totalShift < 0)
            {
                // This means baseExp > -1074, shift left
                value <<= -totalShift;
                mantissaBits = (long)value;
            }
            else
            {
                // Round
                var discarded = value & ((System.Numerics.BigInteger.One << totalShift) - 1);
                var halfway = System.Numerics.BigInteger.One << (totalShift - 1);
                value >>= totalShift;

                if (discarded > halfway || (discarded == halfway && (value & 1) == 1))
                {
                    value += 1;
                }

                mantissaBits = (long)value;
            }

            // Subnormal: biased exponent = 0
            var doubleBits = mantissaBits;
            if (negative) doubleBits |= unchecked((long)0x8000000000000000L);
            return BitConverter.Int64BitsToDouble(doubleBits);
        }
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

#region

using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Number", PrototypeType = typeof(NumberPrototype), Length = 1d, DisplayName = "Number")]
public sealed partial class NumberConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Number constructor not initialized");

    [JsConstructorMethod("isFinite", Length = 1d)]
    public static JsValue IsFinite(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetDouble(out var d))
        {
            return JsValue.False;
        }

        return new JsValue(!double.IsNaN(d) && !double.IsInfinity(d));
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, _constructor ?? ConstructFallback);
            InitializeNumberWrapper(constructing, args);
            return new JsValue(constructing);
        }

        if (args.Count == 0)
        {
            return new JsValue(0d);
        }

        return new JsValue(ToNumberAllowingBigInt(args.GetArgument(0)));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.NumberPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                return new JsValue(args.Count == 0 ? 0d : ToNumberAllowingBigInt(args.GetArgument(0)));
            }

            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget.IsObject ? newTarget.AsObject<IJsCallable>() : null;
            return ConstructWithNewTarget(args, newTargetCallable ?? target, target);
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
        AttachConstants(constructor);
        EnsurePrototypeNumberData();
    }

    private JsValue ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(JsValue.Undefined, false);
        if (instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeNumberWrapper(instance, args);
        return new JsValue(instance);
    }

    private static void InitializeNumberWrapper(JsObject wrapper, IReadOnlyList<JsValue> args)
    {
        var result = args.Count == 0 ? 0d : ToNumberAllowingBigInt(args.GetArgument(0));
        wrapper.SetProperty("__value__", result);
    }

    /// <summary>
    /// <para>
    /// Per ES2024 21.1.1.1 Number(value):
    /// 1. If value is not present, return +0.
    /// 2. Let n be ToNumeric(value).
    /// 3. If n is a BigInt, let n be ℝ(n) (the mathematical value converted to Number).
    /// 4. Return n.
    /// </para>
    /// <para>This differs from ToNumber which throws TypeError for BigInt.</para>
    /// </summary>
    private static double ToNumberAllowingBigInt(JsValue value)
    {
        // First, convert to numeric (Number or BigInt) using ToNumeric semantics
        var numeric = JsOps.ToNumericAsJsValue(value);

        // If it's already a Number, return it
        if (numeric.IsNumber)
        {
            return numeric.NumberValue;
        }

        // If it's a BigInt, convert to double (explicit Number() conversion is allowed)
        if (numeric.IsBigInt)
        {
            var bigInt = numeric.AsBigInt();
            return BigIntToDouble(bigInt.Value);
        }

        // Fallback for other cases (shouldn't happen, but be safe)
        return double.NaN;
    }

    /// <summary>
    /// Converts a BigInteger to double using IEEE 754 round-to-even semantics.
    /// The default C# (double)BigInteger cast truncates rather than rounding,
    /// which gives incorrect results for values exactly halfway between two doubles.
    /// </summary>
    private static double BigIntToDouble(BigInteger value)
    {
        if (value.IsZero)
        {
            return 0.0;
        }

        var negative = value < BigInteger.Zero;
        var abs = BigInteger.Abs(value);

        // For values that fit exactly in a double mantissa (53 bits), the cast is exact.
        // 2^53 = 9007199254740992 is the largest integer where all integers up to it
        // are exactly representable in double. Beyond that, we need proper rounding.
        if (abs <= 9007199254740992L) // 2^53
        {
            return (double)value;
        }

        // For larger values, we need proper IEEE 754 round-to-even.
        // A double has 53 bits of mantissa (including the implicit leading 1).
        // We need to determine if the value rounds up or down.
        var bitLen = (int)abs.GetBitLength();

        if (bitLen > 1024)
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        // Number of bits to discard (beyond the 53-bit mantissa)
        var shift = bitLen - 53;

        if (shift <= 0)
        {
            // Fits exactly in a double mantissa
            return (double)value;
        }

        // Extract the top 53 bits and the rounding bit(s)
        var mantissa = abs >> shift;
        var halfBit = BigInteger.One << (shift - 1);
        var remainder = abs & ((BigInteger.One << shift) - BigInteger.One);

        if (remainder > halfBit)
        {
            // Above halfway: round up
            mantissa += BigInteger.One;
        }
        else if (remainder == halfBit)
        {
            // Exactly halfway: round to even (banker's rounding)
            if (!mantissa.IsEven)
            {
                mantissa += BigInteger.One;
            }
        }
        // else: below halfway, truncate (keep mantissa as is)

        // If rounding caused the mantissa to overflow 53 bits, shift again
        if (mantissa.GetBitLength() > 53)
        {
            mantissa >>= 1;
            shift++;
        }

        // Construct the double: mantissa * 2^shift
        var result2 = (double)mantissa * Math.Pow(2, shift);
        return negative ? -result2 : result2;
    }

    private void EnsurePrototypeNumberData()
    {
        if (Prototype is IJsPropertyAccessor accessor && !accessor.TryGetProperty("__value__", out _))
        {
            accessor.SetProperty("__value__", 0d);
        }
    }

    // JavaScript's Number.EPSILON is 2^-52, NOT C#'s double.Epsilon (which is the smallest positive double ~4.94e-324)
    // Number.EPSILON represents the difference between 1 and the smallest floating point number greater than 1
    private const double JsEpsilon = 2.220446049250313e-16; // 2^-52

    private static void AttachConstants(HostFunction constructor)
    {
        DefineConstantProperty(constructor, "EPSILON", JsEpsilon);
        DefineConstantProperty(constructor, "MAX_SAFE_INTEGER", 9007199254740991d);
        DefineConstantProperty(constructor, "MIN_SAFE_INTEGER", -9007199254740991d);
        DefineConstantProperty(constructor, "MAX_VALUE", double.MaxValue);
        DefineConstantProperty(constructor, "MIN_VALUE", double.Epsilon);
        DefineConstantProperty(constructor, "POSITIVE_INFINITY", double.PositiveInfinity);
        DefineConstantProperty(constructor, "NEGATIVE_INFINITY", double.NegativeInfinity);
        DefineConstantProperty(constructor, "NaN", double.NaN);
    }
}

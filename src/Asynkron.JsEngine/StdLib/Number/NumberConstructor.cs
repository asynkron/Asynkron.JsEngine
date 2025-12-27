#region

using System.Globalization;
using Asynkron.JsEngine.JsTypes;
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
    // Static methods registered via code generation

    [JsConstructorMethod("isInteger", Length = 1d)]
    private static JsValue IsInteger(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetDouble(out var d))
        {
            return JsValue.False;
        }

        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return JsValue.False;
        }

        return new JsValue(Math.Abs(d % 1) < double.Epsilon);
    }

    [JsConstructorMethod("isFinite", Length = 1d)]
    public static JsValue IsFinite(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetDouble(out var d))
        {
            return JsValue.False;
        }

        return new JsValue(!double.IsNaN(d) && !double.IsInfinity(d));
    }

    [JsConstructorMethod("isNaN", Length = 1d)]
    private static JsValue IsNaN(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.False;
        }

        if (!args[0].TryGetDouble(out var d))
        {
            return JsValue.False;
        }

        return new JsValue(double.IsNaN(d));
    }

    [JsConstructorMethod("isSafeInteger", Length = 1d)]
    private static JsValue IsSafeInteger(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetDouble(out var d))
        {
            return JsValue.False;
        }

        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return JsValue.False;
        }

        if (Math.Abs(d % 1) >= double.Epsilon)
        {
            return JsValue.False;
        }

        return new JsValue(Math.Abs(d) <= 9007199254740991);
    }

    [JsConstructorMethod("parseFloat", Length = 1d)]
    private static JsValue ParseFloat(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        var str = args[0].ToJsString();
        str = str.Trim();
        if (str.Length == 0)
        {
            return JsValue.NaN;
        }

        var match = FloatRegex().Match(str);
        if (match.Success)
        {
            if (double.TryParse(match.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var result))
            {
                return new JsValue(result);
            }
        }

        if (str.StartsWith("Infinity", StringComparison.Ordinal))
        {
            return new JsValue(double.PositiveInfinity);
        }

        if (str.StartsWith("+Infinity", StringComparison.Ordinal))
        {
            return new JsValue(double.PositiveInfinity);
        }

        if (str.StartsWith("-Infinity", StringComparison.Ordinal))
        {
            return new JsValue(double.NegativeInfinity);
        }

        return JsValue.NaN;
    }

    [JsConstructorMethod("parseInt", Length = 2d)]
    private static JsValue ParseInt(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        var str = args[0].ToJsString();
        str = str.Trim();
        if (str.Length == 0)
        {
            return JsValue.NaN;
        }

        var radix = args.Count > 1 && args[1].TryGetDouble(out var r) ? (int)r : 10;
        if (radix is < 2 or > 36)
        {
            return JsValue.NaN;
        }

        var sign = 1;
        if (str.StartsWith('-'))
        {
            sign = -1;
            str = str.Substring(1);
        }
        else if (str.StartsWith('+'))
        {
            str = str.Substring(1);
        }

        if (radix == 16 && str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            str = str.Substring(2);
        }

        double parsed = 0;
        foreach (var c in str)
        {
            int digit;
            if (char.IsDigit(c))
            {
                digit = c - '0';
            }
            else if (char.IsLetter(c))
            {
                digit = char.ToUpperInvariant(c) - 'A' + 10;
            }
            else
            {
                break;
            }

            if (digit >= radix)
            {
                break;
            }

            parsed = parsed * radix + digit;
        }

        return new JsValue(parsed * sign);
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

        return new JsValue(JsOps.ToNumber(args.GetArgument(0)));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.NumberPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                return new JsValue(args.Count == 0 ? 0d : JsOps.ToNumber(args.GetArgument(0)));
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
        var result = args.Count == 0 ? 0d : JsOps.ToNumber(args.GetArgument(0));
        wrapper.SetProperty("__value__", result);
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

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        instance.SetPrototype(proto);
    }
}

using System.Collections.Generic;
using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Number", PrototypeType = typeof(NumberPrototype), Length = 1d, DisplayName = "Number")]
public sealed partial class NumberConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is JsObject { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, _constructor ?? ConstructFallback);
            InitializeNumberWrapper(constructing, args);
            return constructing;
        }

        if (args.Count == 0)
        {
            return 0d;
        }

        return JsOps.ToNumber(args.GetArgument(0));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.NumberPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget is null)
            {
                return args.Count == 0 ? 0d : JsOps.ToNumber(args.GetArgument(0));
            }

            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget as IJsCallable ?? target;
            return ConstructWithNewTarget(args, newTargetCallable, target);
        });

        AttachStatics(constructor);
        EnsurePrototypeNumberData();
    }

    private object ConstructWithNewTarget(IReadOnlyList<object?> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(null, assignPrototype: false);
        if (proto is not null && instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeNumberWrapper(instance, args);
        return instance;
    }

    private void InitializeNumberWrapper(JsObject wrapper, IReadOnlyList<object?> args)
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

    private void AttachStatics(HostFunction constructor)
    {
        DefineBuiltinFunction(constructor.PropertiesObject, "isInteger", new HostFunction(NumberIsInteger), 1);
        DefineBuiltinFunction(constructor.PropertiesObject, "isFinite", new HostFunction(NumberIsFinite), 1);
        DefineBuiltinFunction(constructor.PropertiesObject, "isNaN", new HostFunction(NumberIsNaN), 1);
        DefineBuiltinFunction(constructor.PropertiesObject, "isSafeInteger", new HostFunction(NumberIsSafeInteger), 1);
        DefineBuiltinFunction(constructor.PropertiesObject, "parseFloat", new HostFunction(NumberParseFloat), 1);
        DefineBuiltinFunction(constructor.PropertiesObject, "parseInt", new HostFunction(NumberParseInt), 2);

        DefineConstantProperty(constructor, "EPSILON", double.Epsilon);
        DefineConstantProperty(constructor, "MAX_SAFE_INTEGER", 9007199254740991d);
        DefineConstantProperty(constructor, "MIN_SAFE_INTEGER", -9007199254740991d);
        DefineConstantProperty(constructor, "MAX_VALUE", double.MaxValue);
        DefineConstantProperty(constructor, "MIN_VALUE", double.Epsilon);
        DefineConstantProperty(constructor, "POSITIVE_INFINITY", double.PositiveInfinity);
        DefineConstantProperty(constructor, "NEGATIVE_INFINITY", double.NegativeInfinity);
        DefineConstantProperty(constructor, "NaN", double.NaN);
    }

    private object? NumberIsInteger(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not double d)
        {
            return false;
        }

        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return false;
        }

        return Math.Abs(d % 1) < double.Epsilon;
    }

    private object? NumberIsFinite(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not double d)
        {
            return false;
        }

        return !double.IsNaN(d) && !double.IsInfinity(d);
    }

    private object? NumberIsNaN(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        return args[0] is double.NaN;
    }

    private object? NumberIsSafeInteger(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not double d)
        {
            return false;
        }

        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return false;
        }

        if (Math.Abs(d % 1) >= double.Epsilon)
        {
            return false;
        }

        return Math.Abs(d) <= 9007199254740991;
    }

    private object? NumberParseFloat(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return double.NaN;
        }

        var str = args[0]?.ToString() ?? "";
        str = str.Trim();
        if (str.Length == 0)
        {
            return double.NaN;
        }

        var match = FloatRegex().Match(str);
        if (match.Success)
        {
            if (double.TryParse(match.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        if (str.StartsWith("Infinity"))
        {
            return double.PositiveInfinity;
        }

        if (str.StartsWith("+Infinity"))
        {
            return double.PositiveInfinity;
        }

        if (str.StartsWith("-Infinity"))
        {
            return double.NegativeInfinity;
        }

        return double.NaN;
    }

    private object? NumberParseInt(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return double.NaN;
        }

        var str = args[0]?.ToString() ?? "";
        str = str.Trim();
        if (str.Length == 0)
        {
            return double.NaN;
        }

        var radix = args.Count > 1 && args[1] is double r ? (int)r : 10;
        if (radix is < 2 or > 36)
        {
            return double.NaN;
        }

        var sign = 1;
        if (str.StartsWith("-"))
        {
            sign = -1;
            str = str.Substring(1);
        }
        else if (str.StartsWith("+"))
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

        return parsed * sign;
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Number constructor not initialized");

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }
    }
}

#region

using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StringHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("String", PrototypeType = typeof(StringPrototype), Length = 1d, DisplayName = "String")]
public sealed partial class StringConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("String constructor not initialized");
    // Static methods registered via code generation

    [JsConstructorMethod("fromCodePoint", Length = 1d)]
    public static JsValue FromCodePoint(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return new JsValue("");
        }

        var result = new StringBuilder();
        foreach (var arg in args)
        {
            var num = JsOps.ToNumber(arg);
            // Per spec, reject NaN, Infinity, and non-integers.
            if (double.IsNaN(num) || double.IsInfinity(num) || num % 1 != 0)
            {
                throw StandardLibrary.ThrowRangeError("Invalid code point");
            }

            var codePoint = (int)num;
            if (codePoint is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
            {
                throw StandardLibrary.ThrowRangeError($"Invalid code point {codePoint}");
            }

            if (codePoint <= 0xFFFF)
            {
                result.Append((char)codePoint);
            }
            else
            {
                codePoint -= 0x10000;
                result.Append((char)(0xD800 + (codePoint >> 10)));
                result.Append((char)(0xDC00 + (codePoint & 0x3FF)));
            }
        }

        return new JsValue(result.ToString());
    }

    [JsConstructorMethod("fromCharCode", Length = 1d)]
    public static JsValue FromCharCode(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return new JsValue("");
        }

        var result = new StringBuilder();
        foreach (var arg in args)
        {
            var num = JsOps.ToNumber(arg);
            // ToUint16 behavior: NaN/Infinity -> +0, then mod 2^16.
            var charCode = ToUint16(num);
            result.Append((char)charCode);
        }

        return new JsValue(result.ToString());
    }

    [JsConstructorMethod("raw", Length = 1d)]
    public static JsValue Raw(IReadOnlyList<JsValue> args)
    {
        // Step 1-2: Let cooked be ? ToObject(template)
        var templateArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (templateArg.IsNullOrUndefined)
        {
            throw StandardLibrary.ThrowTypeError("Cannot convert undefined or null to object");
        }

        if (!templateArg.TryGetObject<IJsPropertyAccessor>(out var template))
        {
            throw StandardLibrary.ThrowTypeError("String.raw requires an object as first argument");
        }

        // Step 3: Let raw be ? ToObject(? Get(cooked, "raw"))
        if (!template.TryGetProperty("raw", out var rawValue))
        {
            throw StandardLibrary.ThrowTypeError("String.raw: template.raw is not an object");
        }

        if (rawValue.IsNullOrUndefined)
        {
            throw StandardLibrary.ThrowTypeError("Cannot convert undefined or null to object");
        }

        if (!rawValue.TryGetObject<IJsPropertyAccessor>(out var rawAccessor))
        {
            throw StandardLibrary.ThrowTypeError("String.raw: template.raw is not an object");
        }

        // Step 4: Let literalSegments be ? ToLength(? Get(raw, "length"))
        double rawLength = 0;
        if (rawAccessor is JsArray rawArray)
        {
            rawLength = rawArray.Length;
        }
        else if (rawAccessor.TryGetProperty("length", out var lengthVal))
        {
            // ToLength: min(max(ToIntegerOrInfinity(x), 0), 2^53 - 1)
            var len = JsOps.ToNumber(lengthVal);
            if (double.IsNaN(len) || len <= 0) len = 0;
            else if (double.IsPositiveInfinity(len)) len = 9007199254740991; // 2^53 - 1
            else len = Math.Floor(Math.Min(len, 9007199254740991));
            rawLength = len;
        }

        // Step 5: If literalSegments <= 0, return ""
        if (rawLength <= 0)
        {
            return new JsValue("");
        }

        var literalSegments = (int)Math.Min(rawLength, int.MaxValue);

        // Build raw items list
        var rawItems = new List<JsValue>(literalSegments);
        for (var i = 0; i < literalSegments; i++)
        {
            if (rawAccessor.TryGetProperty(i.ToString(CultureInfo.InvariantCulture), out var item))
            {
                rawItems.Add(item);
            }
            else
            {
                rawItems.Add(JsValue.Undefined);
            }
        }

        var result = new StringBuilder();
        for (var i = 0; i < rawItems.Count; i++)
        {
            result.Append(JsOps.ToJsString(rawItems[i]));
            if (i + 1 < args.Count)
            {
                result.Append(JsOps.ToJsString(args[i + 1]));
            }
        }

        return new JsValue(result.ToString());
    }

    [JsConstructorMethod("escape", Length = 1d)]
    public static JsValue Escape(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return new JsValue("");
        }

        var value = JsOps.ToJsString(args[0]);
        var result = new StringBuilder();

        foreach (var ch in value)
        {
            switch (ch)
            {
                case ' ': result.Append("%20"); break;
                case '!': result.Append("%21"); break;
                case '"': result.Append("%22"); break;
                case '#': result.Append("%23"); break;
                case '$': result.Append("%24"); break;
                case '%': result.Append("%25"); break;
                case '&': result.Append("%26"); break;
                case '\'': result.Append("%27"); break;
                case '(': result.Append("%28"); break;
                case ')': result.Append("%29"); break;
                case '+': result.Append("%2B"); break;
                case ',': result.Append("%2C"); break;
                case '/': result.Append("%2F"); break;
                case ':': result.Append("%3A"); break;
                case ';': result.Append("%3B"); break;
                case '<': result.Append("%3C"); break;
                case '=': result.Append("%3D"); break;
                case '>': result.Append("%3E"); break;
                case '?': result.Append("%3F"); break;
                case '@': result.Append("%40"); break;
                case '[': result.Append("%5B"); break;
                case '\\': result.Append("%5C"); break;
                case ']': result.Append("%5D"); break;
                case '^': result.Append("%5E"); break;
                case '`': result.Append("%60"); break;
                case '{': result.Append("%7B"); break;
                case '|': result.Append("%7C"); break;
                case '}': result.Append("%7D"); break;
                case '~': result.Append("%7E"); break;
                default:
                    if (ch > 127)
                    {
                        result.Append("%u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(ch);
                    }

                    break;
            }
        }

        return new JsValue(result.ToString());
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, _constructor ?? ConstructFallback);
            InitializeWrapper(constructing, args);
            return new JsValue(constructing);
        }

        return new JsValue(ResolveString(args));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.StringPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                return new JsValue(ResolveString(args));
            }

            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget.IsObject ? newTarget.AsObject<IJsCallable>() : null;
            return ConstructWithNewTarget(args, newTargetCallable ?? target, target);
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
    }

    private JsValue ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(JsValue.Undefined, false);
        if (instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeWrapper(instance, args);
        return new JsValue(instance);
    }

    private void InitializeWrapper(JsObject wrapper, IReadOnlyList<JsValue> args)
    {
        // Per spec: new String(value) calls ToString(value) which throws TypeError for Symbols.
        var str = ResolveStringForConstruct(args);
        InitializeStringWrapper(str, wrapper, Realm);
    }

    /// <summary>
    /// Used when String() is called as a function (without new).
    /// Per spec 22.1.1.1: If NewTarget is undefined and Type(value) is Symbol,
    /// return SymbolDescriptiveString(value).
    /// </summary>
    private string ResolveString(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return string.Empty;
        }

        var value = args.GetArgument(0);
        if (value is { IsSymbol: true, ObjectValue: JsSymbol typedSymbol })
        {
            return typedSymbol.ToString();
        }

        var context = Realm.CreateContext();
        var str = JsOps.ToJsString(value, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return str;
    }

    /// <summary>
    /// Used when String() is called as a constructor (with new).
    /// Per spec 22.1.1.1: Let s be ? ToString(value). This throws TypeError for Symbols.
    /// </summary>
    private string ResolveStringForConstruct(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return string.Empty;
        }

        var value = args.GetArgument(0);
        // No special Symbol handling here -- ToString(Symbol) throws TypeError per spec.
        var context = Realm.CreateContext();
        var str = JsOps.ToJsString(value, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return str;
    }

    private static int ToUint16(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number) || number == 0)
        {
            return 0;
        }

        // Convert to a truncated integer, then wrap into 16-bit range.
        var integer = Math.Sign(number) * Math.Floor(Math.Abs(number));
        var modulo = (long)integer % 65536;
        if (modulo < 0)
        {
            modulo += 65536;
        }

        return (int)modulo;
    }
}

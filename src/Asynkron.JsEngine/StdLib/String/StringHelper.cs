using System.Globalization;
using System.Text;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public static class StringHelper
{
    internal static JsObject InitializeStringWrapper(string str, JsObject wrapper, RealmState? realm = null)
    {
        wrapper.SetProperty("__value__", str);

        wrapper.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)str.Length,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            });
        wrapper.SetVirtualPropertyProvider(new StringVirtualPropertyProvider(str));
        wrapper.RealmState ??= realm;
        return wrapper;
    }

    internal static string RequireStringReceiver(object? receiver, RealmState? realm = null)
    {
        return receiver switch
        {
            string s => s,
            JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner.TryGetString(out var s) => s,
            IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var inner)
                                              && inner.TryGetString(out var s) => s,
            _ => throw ThrowTypeError("String.prototype valueOf called on non-string object", realm: realm)
        };
    }

    /// <summary>
    /// JsValue overload for RequireStringReceiver.
    /// </summary>
    internal static string RequireStringReceiver(JsValue receiver, RealmState? realm = null)
    {
        // Fast path for string kind
        if (receiver.Kind == JsValueKind.String)
        {
            return receiver.ObjectValue as string ?? string.Empty;
        }

        // For objects, check for __value__ property
        if (receiver is { Kind: JsValueKind.Object, ObjectValue: IJsPropertyAccessor accessor })
        {
            if (accessor.TryGetProperty("__value__", out var inner) && inner.TryGetString(out var s))
            {
                return s;
            }
        }

        throw ThrowTypeError("String.prototype valueOf called on non-string object", realm: realm);
    }

    /// <summary>
    ///     Creates a string wrapper object with string methods attached.
    ///     This allows string primitives to have methods like toLowerCase(), substring(), etc.
    /// </summary>
    public static JsObject CreateStringWrapper(string str, EvaluationContext? context = null, RealmState? realm = null)
    {
        var stringObj = InitializeStringWrapper(str, new JsObject(), realm);

        var realmState = realm ?? context?.RealmState;
        var prototype = realmState?.StringPrototype;
        if (prototype is not null)
        {
            stringObj.SetPrototype(prototype);
        }

        return stringObj;
    }

    internal static object? StringFromCodePoint(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        var result = new StringBuilder();
        foreach (var arg in args)
        {
            var num = JsOps.ToNumber(arg);
            if (double.IsNaN(num) || double.IsInfinity(num))
            {
                continue;
            }

            var codePoint = (int)num;
            if (codePoint is < 0 or > 0x10FFFF)
            {
                throw new Exception("RangeError: Invalid code point " + codePoint);
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

        return result.ToString();
    }

    internal static object? StringFromCharCode(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        var result = new StringBuilder();
        foreach (var arg in args)
        {
            var num = JsOps.ToNumber(arg);
            if (double.IsNaN(num) || double.IsInfinity(num))
            {
                continue;
            }

            var charCode = (int)num & 0xFFFF;
            result.Append((char)charCode);
        }

        return result.ToString();
    }

    internal static string StringRaw(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        if (!args[0].TryGetObject<IJsPropertyAccessor>(out var template))
        {
            return "";
        }

        if (!template.TryGetProperty("raw", out var rawValue) || !rawValue.TryGetObject<IJsPropertyAccessor>(out var rawAccessor))
        {
            return "";
        }

        // Get items from the raw accessor - could be JsArray or JsObject
        IReadOnlyList<JsValue>? rawItems = null;
        if (rawAccessor is JsArray rawArray)
        {
            rawItems = rawArray.Items;
        }
        else if (rawAccessor is JsObject rawObj && rawObj.TryGetProperty("length", out var lengthVal))
        {
            var length = (int)JsOps.ToNumber(lengthVal);
            var items = new List<JsValue>(length);
            for (var i = 0; i < length; i++)
            {
                if (rawObj.TryGetProperty(i.ToString(CultureInfo.InvariantCulture), out var item))
                {
                    items.Add(item);
                }
                else
                {
                    items.Add(JsValue.Undefined);
                }
            }
            rawItems = items;
        }

        if (rawItems == null)
        {
            return "";
        }

        var result = new StringBuilder();
        var rawCount = rawItems.Count;

        for (var i = 0; i < rawCount; i++)
        {
            var rawPart = JsOps.ToJsString(rawItems[i]);
            result.Append(rawPart);

            if (i >= args.Count - 1)
            {
                break;
            }

            var substitution = JsOps.ToJsString(args[i + 1]);
            result.Append(substitution);
        }

        return result.ToString();
    }

    internal static string StringEscape(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        var value = JsOps.ToJsString(args[0]);
        var result = new StringBuilder();

        foreach (var ch in value)
        {
            switch (ch)
            {
                case ' ':
                    result.Append("%20");
                    break;
                case '!':
                    result.Append("%21");
                    break;
                case '"':
                    result.Append("%22");
                    break;
                case '#':
                    result.Append("%23");
                    break;
                case '$':
                    result.Append("%24");
                    break;
                case '%':
                    result.Append("%25");
                    break;
                case '&':
                    result.Append("%26");
                    break;
                case '\'':
                    result.Append("%27");
                    break;
                case '(':
                    result.Append("%28");
                    break;
                case ')':
                    result.Append("%29");
                    break;
                case '*':
                    result.Append("%2A");
                    break;
                case '+':
                    result.Append("%2B");
                    break;
                case ',':
                    result.Append("%2C");
                    break;
                case '/':
                    result.Append("%2F");
                    break;
                case ':':
                    result.Append("%3A");
                    break;
                case ';':
                    result.Append("%3B");
                    break;
                case '<':
                    result.Append("%3C");
                    break;
                case '=':
                    result.Append("%3D");
                    break;
                case '>':
                    result.Append("%3E");
                    break;
                case '?':
                    result.Append("%3F");
                    break;
                case '@':
                    result.Append("%40");
                    break;
                case '[':
                    result.Append("%5B");
                    break;
                case '\\':
                    result.Append("%5C");
                    break;
                case ']':
                    result.Append("%5D");
                    break;
                case '^':
                    result.Append("%5E");
                    break;
                case '_':
                    result.Append("%5F");
                    break;
                case '`':
                    result.Append("%60");
                    break;
                case '{':
                    result.Append("%7B");
                    break;
                case '|':
                    result.Append("%7C");
                    break;
                case '}':
                    result.Append("%7D");
                    break;
                case '~':
                    result.Append("%7E");
                    break;
                default:
                    if (ch <= 0x7F)
                    {
                        result.Append(ch);
                    }
                    else
                    {
                        result.Append("%u");
                        result.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    ///     Creates the String constructor with static methods.
    /// </summary>
    public static HostFunction CreateStringConstructor(RealmState realm)
    {
        return StringConstructor.CreateConstructor(realm);
    }

    private sealed class StringVirtualPropertyProvider(string value) : IVirtualPropertyProvider
    {
        public bool TryGetOwnProperty(string name, out object? valueOut, out PropertyDescriptor? descriptor)
        {
            valueOut = null;
            descriptor = null;

            if (!IsArrayIndex(name, out var index) || index < 0 || index >= value.Length)
            {
                return false;
            }

            var ch = value[index].ToString();
            valueOut = ch;
            descriptor = new PropertyDescriptor
            {
                Value = ch,
                Writable = false,
                Enumerable = true,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            };
            return true;
        }

        public IEnumerable<string> GetEnumerableKeys()
        {
            for (var i = 0; i < value.Length; i++)
            {
                yield return i.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool IsArrayIndex(string key, out int index)
        {
            return int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
        }
    }
}

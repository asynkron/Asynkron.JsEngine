#region

using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

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

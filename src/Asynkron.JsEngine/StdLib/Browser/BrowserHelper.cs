#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class BrowserHelper
{
    public static JsObject CreateLocalStorageObject()
    {
        var storage = new JsObject();
        var backing = new Dictionary<string, string?>(StringComparer.Ordinal);

        storage.SetHostedProperty("getItem", GetItem);
        storage.SetHostedProperty("setItem", SetItem);
        storage.SetHostedProperty("removeItem", RemoveItem);
        storage.SetHostedProperty("clear", Clear);

        return storage;

        JsValue GetItem(JsValue _, IReadOnlyList<JsValue> args)
        {
            if (args.Count == 0)
            {
                return JsValue.Null;
            }

            var key = JsOps.ToJsString(args[0]);
            var result = backing.GetValueOrDefault(key);
            return result is not null ? new JsValue(result) : JsValue.Null;
        }

        JsValue SetItem(JsValue _, IReadOnlyList<JsValue> args)
        {
            if (args.Count < 2)
            {
                return JsValue.Undefined;
            }

            var key = JsOps.ToJsString(args[0]);
            var value = JsOps.ToJsString(args[1]);
            backing[key] = value;
            return JsValue.Undefined;
        }

        JsValue RemoveItem(JsValue _, IReadOnlyList<JsValue> args)
        {
            if (args.Count == 0)
            {
                return JsValue.Undefined;
            }

            var key = JsOps.ToJsString(args[0]);
            backing.Remove(key);
            return JsValue.Undefined;
        }

        JsValue Clear(JsValue _, IReadOnlyList<JsValue> __)
        {
            backing.Clear();
            return JsValue.Undefined;
        }
    }
}

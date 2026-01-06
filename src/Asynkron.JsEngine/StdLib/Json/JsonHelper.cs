#region

using System.Globalization;
using System.Text.Json;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class JsonHelper
{
    /// <summary>
    /// JsValue-returning overload for ParseJsonWithReviver that avoids boxing.
    /// </summary>
    internal static JsValue ParseJsonWithReviverJsValue(string jsonStr, RealmState realm, EvaluationContext? context,
        JsValue reviverValue)
    {
        JsValue parsed;
        try
        {
            parsed = ParseJsonValue(JsonDocument.Parse(jsonStr).RootElement, realm);
        }
        catch
        {
            throw ThrowSyntaxError("Unexpected token in JSON", context, realm);
        }

        if (!reviverValue.TryGetObject<IJsCallable>(out var reviver))
        {
            return parsed;
        }

        var holder = new JsObject();
        holder.SetProperty("", parsed);

        return ApplyJsonReviverJsValue(reviver, holder, "");
    }

    private static JsValue ParseJsonValue(JsonElement element, RealmState realm)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsObject(realm.ObjectPrototype);
                foreach (var prop in element.EnumerateObject())
                {
                    obj.SetProperty(prop.Name, ParseJsonValue(prop.Value, realm));
                }

                return new JsValue(obj);

            case JsonValueKind.Array:
                var arr = new JsArray(realm);
                foreach (var item in element.EnumerateArray())
                {
                    arr.Push(ParseJsonValue(item, realm));
                }

                return JsValue.FromJsArray(arr);

            case JsonValueKind.String:
                return new JsValue(element.GetString() ?? string.Empty);

            case JsonValueKind.Number:
                return new JsValue(element.GetDouble());

            case JsonValueKind.True:
                return JsValue.True;

            case JsonValueKind.False:
                return JsValue.False;

            case JsonValueKind.Null:
            default:
                return JsValue.Null;
        }
    }

    private static JsValue ApplyJsonReviverJsValue(IJsCallable reviver, IJsObjectLike holder, string name)
    {
        if (!holder.TryGetProperty(name, out var value))
        {
            value = JsValue.Null;
        }

        if (value.TryGetObject<JsObject>(out var jsObj))
        {
            foreach (var key in jsObj.Keys.ToArray())
            {
                var revived = ApplyJsonReviverJsValue(reviver, jsObj, key);
                if (revived.IsUndefined)
                {
                    jsObj.Delete(key);
                }
                else
                {
                    jsObj.SetProperty(key, revived);
                }
            }
        }
        else if (value.TryGetObject<JsArray>(out var arr))
        {
            var length = (int)arr.Length;
            for (var i = 0; i < length; i++)
            {
                var revived = ApplyJsonReviverJsValue(reviver, arr,
                    i.ToString(CultureInfo.InvariantCulture));
                if (revived.IsUndefined)
                {
                    arr.DeleteElement(i);
                }
                else
                {
                    arr.SetElement(i, revived);
                }
            }
        }

        return reviver.Invoke([new JsValue(name), value], JsValue.FromObjectUnsafe(holder));
    }

    internal static string StringifyValue(object? value, int depth = 0)
    {
        while (true)
        {
            if (depth > 100)
            {
                return "null"; // Prevent stack overflow
            }

            switch (value)
            {
                case null:
                    return "null";

                case JsValue jsValue:
                    // Unwrap JsValue based on kind to avoid boxing
                    if (jsValue.IsNullOrUndefined)
                    {
                        return "null";
                    }

                    switch (jsValue.Kind)
                    {
                        case JsValueKind.Boolean:
                            return jsValue.NumberValue != 0 ? "true" : "false";
                        case JsValueKind.Number:
                            {
                                var d = jsValue.NumberValue;
                                if (double.IsNaN(d) || double.IsInfinity(d))
                                {
                                    return "null";
                                }

                                return d.ToString(CultureInfo.InvariantCulture);
                            }
                        case JsValueKind.String when jsValue.ObjectValue is string str:
                            return JsonSerializer.Serialize(str);
                        default:
                            // For objects and other types, continue with the underlying object
                            value = jsValue.ObjectValue;
                            continue;
                    }

                case bool b:
                    return b ? "true" : "false";

                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        return "null";
                    }

                    return d.ToString(CultureInfo.InvariantCulture);

                case string s:
                    return JsonSerializer.Serialize(s);

                case JsArray arr:
                    var arrItems = new List<string>();
                    foreach (var item in arr.Items)
                    {
                        arrItems.Add(StringifyValue(item, depth + 1));
                    }

                    return "[" + string.Join(',', arrItems) + "]";

                case JsObject obj:
                    var objProps = new List<string>();
                    foreach (var kvp in obj)
                    {
                        // Skip functions and internal properties
                        if (kvp.Value is IJsCallable || kvp.Key.StartsWith('_'))
                        {
                            continue;
                        }

                        var key = JsonSerializer.Serialize(kvp.Key);
                        var val = StringifyValue(kvp.Value, depth + 1);
                        objProps.Add($"{key}:{val}");
                    }

                    return "{" + string.Join(',', objProps) + "}";

                case IJsCallable:
                    return "undefined";

                default:
                    return JsonSerializer.Serialize(value?.ToString() ?? "");
            }
        }
    }
}

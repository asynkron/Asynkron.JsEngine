using System.Globalization;
using System.Text.Json;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static object? ParseJsonWithReviver(string jsonStr, RealmState realm, EvaluationContext? context,
        object? reviverCandidate)
    {
        object? parsed;
        try
        {
            parsed = ParseJsonValue(JsonDocument.Parse(jsonStr).RootElement, realm);
        }
        catch
        {
            throw ThrowSyntaxError("Unexpected token in JSON", context, realm);
        }

        if (reviverCandidate is not IJsCallable reviver)
        {
            return parsed;
        }

        var holder = new JsObject();
        holder.SetProperty("", parsed);

        return ApplyJsonReviver(reviver, holder, "", context, realm);
    }

    private static object? ParseJsonValue(JsonElement element, RealmState realm)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsObject(realm.ObjectPrototype);
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ParseJsonValue(prop.Value, realm);
                }

                return obj;

            case JsonValueKind.Array:
                var arr = new JsArray(realm);
                foreach (var item in element.EnumerateArray())
                {
                    arr.Push(ParseJsonValue(item, realm));
                }

                return arr;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    private static object? ApplyJsonReviver(IJsCallable reviver, IJsObjectLike holder, string name,
        EvaluationContext? context, RealmState realm)
    {
        if (!holder.TryGetProperty(name, out var value))
        {
            value = null;
        }

        switch (value)
        {
            case JsObject obj:
            {
                foreach (var key in obj.Keys.ToArray())
                {
                    var revived = ApplyJsonReviver(reviver, obj, key, context, realm);
                    if (ReferenceEquals(revived, Symbol.Undefined))
                    {
                        obj.Delete(key);
                    }
                    else
                    {
                        obj.SetProperty(key, revived);
                    }
                }

                break;
            }
            case JsArray arr:
            {
                var length = (int)arr.Length;
                for (var i = 0; i < length; i++)
                {
                    var revived = ApplyJsonReviver(reviver, arr,
                        i.ToString(CultureInfo.InvariantCulture), context, realm);
                    if (ReferenceEquals(revived, Symbol.Undefined))
                    {
                        arr.DeleteElement(i);
                    }
                    else
                    {
                        arr.SetElement(i, revived);
                    }
                }

                break;
            }
        }

        var replacement = reviver.Invoke([new JsValue(name), JsValue.FromObject(value)], JsValue.FromObject(holder));
        return replacement.ToObject();
    }

    internal static string StringifyValue(object? value, int depth = 0)
    {
        if (depth > 100)
        {
            return "null"; // Prevent stack overflow
        }

        switch (value)
        {
            case null:
                return "null";

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

                return "[" + string.Join(",", arrItems) + "]";

            case JsObject obj:
                var objProps = new List<string>();
                foreach (var kvp in obj)
                {
                    // Skip functions and internal properties
                    if (kvp.Value is IJsCallable || kvp.Key.StartsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var key = JsonSerializer.Serialize(kvp.Key);
                    var val = StringifyValue(kvp.Value, depth + 1);
                    objProps.Add($"{key}:{val}");
                }

                return "{" + string.Join(",", objProps) + "}";

            case IJsCallable:
                return "undefined";

            default:
                return JsonSerializer.Serialize(value?.ToString() ?? "");
        }
    }
}

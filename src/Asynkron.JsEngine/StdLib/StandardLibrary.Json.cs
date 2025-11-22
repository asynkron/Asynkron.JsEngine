using System.Globalization;
using System.Text.Json;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateJsonObject()
    {
        var json = new JsObject();

        // JSON.parse()
        json["parse"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string jsonStr)
            {
                return null;
            }

            try
            {
                return ParseJsonValue(JsonDocument.Parse(jsonStr).RootElement);
            }
            catch
            {
                // In real JavaScript, this would throw a SyntaxError
                return null;
            }
        });

        // JSON.stringify()
        json["stringify"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "undefined";
            }

            var value = args[0];

            // Handle replacer function and space arguments if needed
            // For now, implement basic stringify
            return StringifyValue(value);
        });

        return json;
    }

    private static object? ParseJsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ParseJsonValue(prop.Value);
                }

                return obj;

            case JsonValueKind.Array:
                var arr = new JsArray();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Push(ParseJsonValue(item));
                }

                AddArrayMethods(arr);
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

    private static string StringifyValue(object? value, int depth = 0)
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
                    if (kvp.Value is IJsCallable || kvp.Key.StartsWith("_"))
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

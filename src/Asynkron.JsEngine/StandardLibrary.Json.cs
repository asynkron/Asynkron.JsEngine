using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

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
                return ParseJsonValue(System.Text.Json.JsonDocument.Parse(jsonStr).RootElement);
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

    private static object? ParseJsonValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var obj = new JsObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ParseJsonValue(prop.Value);
                }

                return obj;

            case System.Text.Json.JsonValueKind.Array:
                var arr = new JsArray();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Push(ParseJsonValue(item));
                }

                AddArrayMethods(arr);
                return arr;

            case System.Text.Json.JsonValueKind.String:
                return element.GetString();

            case System.Text.Json.JsonValueKind.Number:
                return element.GetDouble();

            case System.Text.Json.JsonValueKind.True:
                return true;

            case System.Text.Json.JsonValueKind.False:
                return false;

            case System.Text.Json.JsonValueKind.Null:
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

                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case string s:
                return System.Text.Json.JsonSerializer.Serialize(s);

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

                    var key = System.Text.Json.JsonSerializer.Serialize(kvp.Key);
                    var val = StringifyValue(kvp.Value, depth + 1);
                    objProps.Add($"{key}:{val}");
                }

                return "{" + string.Join(",", objProps) + "}";

            case IJsCallable:
                return "undefined";

            default:
                return System.Text.Json.JsonSerializer.Serialize(value?.ToString() ?? "");
        }
    }

}

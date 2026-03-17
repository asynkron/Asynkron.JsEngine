#region

using System.Globalization;
using System.Text.Json;
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

    #region JSON.stringify (ECMA-262 25.5.2)

    /// <summary>
    /// Context object for the JSON.stringify algorithm, carrying the replacer, gap, and visited stack.
    /// Corresponds to the abstract closure state in ECMA-262 25.5.2.
    /// </summary>
    private sealed class StringifyState
    {
        /// <summary>Replacer function, if the second argument was callable.</summary>
        internal IJsCallable? ReplacerFunction;

        /// <summary>Property filter list, if the second argument was an array.</summary>
        internal List<string>? PropertyList;

        /// <summary>Indentation gap string (up to 10 chars).</summary>
        internal string Gap = string.Empty;

        /// <summary>Current indentation level string.</summary>
        internal string Indent = string.Empty;

        /// <summary>Visited object stack for circular reference detection.</summary>
        internal readonly HashSet<object> Stack = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Entry point for JSON.stringify. Implements ECMA-262 25.5.2 JSON.stringify(value, replacer, space).
    /// Returns JsValue.Undefined when the top-level value would produce undefined per spec.
    /// </summary>
    internal static JsValue Stringify(JsValue value, JsValue replacerArg, JsValue spaceArg)
    {
        var state = new StringifyState();

        // Step 4: Process replacer
        if (replacerArg.TryGetObject<IJsCallable>(out var replacerFn))
        {
            state.ReplacerFunction = replacerFn;
        }
        else if (replacerArg.TryGetObject<JsArray>(out var replacerArr))
        {
            state.PropertyList = BuildPropertyList(replacerArr);
        }

        // Step 5-8: Process space
        var space = spaceArg;

        // Unwrap Number/String wrapper objects
        if (space.IsObject && space.TryGetObject<JsObject>(out var spaceObj))
        {
            if (spaceObj.TryGetProperty("__value__", out var innerVal))
            {
                space = innerVal;
            }
        }

        if (space.IsNumber)
        {
            var spaceCount = Math.Min(10, (int)Math.Max(0, space.NumberValue));
            state.Gap = spaceCount > 0 ? new string(' ', spaceCount) : string.Empty;
        }
        else if (space.IsString)
        {
            var spaceStr = space.AsString();
            state.Gap = spaceStr.Length <= 10 ? spaceStr : spaceStr.Substring(0, 10);
        }

        // Step 9: Create wrapper object
        var wrapper = new JsObject();
        wrapper.SetProperty("", value);

        // Step 10: Call SerializeJSONProperty with the wrapper
        var result = SerializeJsonProperty(state, "", wrapper);

        // If result is null, the spec says return undefined
        return result is null ? JsValue.Undefined : new JsValue(result);
    }

    /// <summary>
    /// Builds the PropertyList from a replacer array (ECMA-262 25.5.2 step 4.b).
    /// </summary>
    private static List<string> BuildPropertyList(JsArray arr)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var length = (int)arr.Length;
        for (var i = 0; i < length; i++)
        {
            var item = arr.GetElement(i);
            string? itemStr = null;

            if (item.IsString)
            {
                itemStr = item.AsString();
            }
            else if (item.IsNumber)
            {
                itemStr = item.NumberValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (item.IsObject && item.TryGetObject<JsObject>(out var obj) &&
                     obj.TryGetProperty("__value__", out var inner))
            {
                // Unwrap String/Number wrapper objects
                if (inner.IsString)
                {
                    itemStr = inner.AsString();
                }
                else if (inner.IsNumber)
                {
                    itemStr = inner.NumberValue.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (itemStr is not null && seen.Add(itemStr))
            {
                list.Add(itemStr);
            }
        }

        return list;
    }

    /// <summary>
    /// Implements SerializeJSONProperty (ECMA-262 25.5.2.5).
    /// Returns null when the value should be omitted (undefined, function, symbol at top level or in object).
    /// </summary>
    private static string? SerializeJsonProperty(StringifyState state, string key, IJsPropertyAccessor holder)
    {
        // Step 1: Let value be ? Get(holder, key)
        holder.TryGetProperty(key, out var value);

        // Step 1b: Check for rawJSON marker (JSON.rawJSON objects)
        if (value.IsObject && value.TryGetObject<JsObject>(out var rawJsonCheck))
        {
            if (rawJsonCheck.TryGetProperty("[[IsRawJSON]]", out var rawMarker) &&
                rawMarker.IsBoolean && rawMarker.NumberValue != 0 &&
                rawJsonCheck.TryGetProperty("rawJSON", out var rawText) && rawText.IsString)
            {
                return rawText.AsString()!;
            }
        }

        // Step 2: If value is an Object or BigInt, check for toJSON
        if (value.IsObject && value.TryGetObject<IJsPropertyAccessor>(out var toJsonHolder))
        {
            if (toJsonHolder.TryGetProperty("toJSON", out var toJson) &&
                toJson.TryGetObject<IJsCallable>(out var toJsonFn))
            {
                value = toJsonFn.Invoke([new JsValue(key)], value);
            }
        }

        // Step 3: If ReplacerFunction exists, call it
        if (state.ReplacerFunction is not null)
        {
            value = state.ReplacerFunction.Invoke(
                [new JsValue(key), value],
                JsValue.FromObjectUnsafe((object)holder));
        }

        // Step 4: Unwrap wrapper objects (Number, String, Boolean, BigInt)
        if (value.IsObject && value.TryGetObject<JsObject>(out var wrapperObj))
        {
            if (wrapperObj.TryGetProperty("__value__", out var innerVal))
            {
                if (innerVal.IsNumber)
                {
                    value = innerVal;
                }
                else if (innerVal.IsString)
                {
                    value = innerVal;
                }
                else if (innerVal.IsBoolean)
                {
                    value = innerVal;
                }
                else if (innerVal.IsBigInt)
                {
                    value = innerVal;
                }
            }
        }

        // Step 5-9: Handle special values
        if (value.IsNull)
        {
            return "null";
        }

        if (value.IsBoolean)
        {
            return value.NumberValue != 0 ? "true" : "false";
        }

        if (value.IsString)
        {
            return QuoteString(value.AsString());
        }

        if (value.IsNumber)
        {
            var d = value.NumberValue;
            if (!double.IsNaN(d) && !double.IsInfinity(d))
            {
                return FormatNumber(d);
            }

            return "null";
        }

        // Step 10: BigInt - throw TypeError
        if (value.IsBigInt)
        {
            throw ThrowTypeError("Do not know how to serialize a BigInt");
        }

        // Step 11: If value is undefined, a function, or a symbol, return null (omit)
        if (value.IsUndefined || value.IsSymbol)
        {
            return null;
        }

        if (value.IsObject && value.TryGetObject<IJsCallable>(out _))
        {
            // Functions are omitted (return null means "undefined" in the spec)
            return null;
        }

        // Step 12-13: Arrays and Objects
        if (value.TryGetObject<JsArray>(out var arr))
        {
            return SerializeJsonArray(state, arr);
        }

        if (value.TryGetObject<IJsPropertyAccessor>(out var objAccessor))
        {
            return SerializeJsonObject(state, objAccessor);
        }

        // Fallback for other object types - should not normally be reached
        return null;
    }

    /// <summary>
    /// Implements SerializeJSONObject (ECMA-262 25.5.2.6).
    /// </summary>
    private static string SerializeJsonObject(StringifyState state, IJsPropertyAccessor obj)
    {
        // Circular reference check
        if (!state.Stack.Add(obj))
        {
            throw ThrowTypeError("Converting circular structure to JSON");
        }

        var stepback = state.Indent;
        state.Indent = stepback + state.Gap;

        List<string> keys;
        if (state.PropertyList is not null)
        {
            keys = state.PropertyList;
        }
        else
        {
            // Get own enumerable property keys in order
            keys = [];
            if (obj is JsObject jsObj)
            {
                foreach (var k in jsObj.GetOwnEnumerablePropertyKeysInOrder(false))
                {
                    keys.Add(k);
                }
            }
            else if (obj is IJsObjectLike objLike)
            {
                foreach (var k in objLike.Keys)
                {
                    keys.Add(k);
                }
            }
        }

        var partial = new List<string>();
        foreach (var propKey in keys)
        {
            var strP = SerializeJsonProperty(state, propKey, obj);
            if (strP is not null)
            {
                var member = QuoteString(propKey) + ":";
                if (state.Gap.Length > 0)
                {
                    member += " ";
                }

                member += strP;
                partial.Add(member);
            }
        }

        string result;
        if (partial.Count == 0)
        {
            result = "{}";
        }
        else if (state.Gap.Length == 0)
        {
            result = "{" + string.Join(',', partial) + "}";
        }
        else
        {
            var separator = ",\n" + state.Indent;
            var properties = string.Join(separator, partial);
            result = "{\n" + state.Indent + properties + "\n" + stepback + "}";
        }

        state.Indent = stepback;
        state.Stack.Remove(obj);
        return result;
    }

    /// <summary>
    /// Implements SerializeJSONArray (ECMA-262 25.5.2.7).
    /// </summary>
    private static string SerializeJsonArray(StringifyState state, JsArray arr)
    {
        // Circular reference check
        if (!state.Stack.Add(arr))
        {
            throw ThrowTypeError("Converting circular structure to JSON");
        }

        var stepback = state.Indent;
        state.Indent = stepback + state.Gap;

        var partial = new List<string>();
        var length = (int)arr.Length;

        for (var index = 0; index < length; index++)
        {
            var strP = SerializeJsonProperty(state, index.ToString(CultureInfo.InvariantCulture), arr);
            // In arrays, undefined/function/symbol serialize as "null" (not omitted)
            partial.Add(strP ?? "null");
        }

        string result;
        if (partial.Count == 0)
        {
            result = "[]";
        }
        else if (state.Gap.Length == 0)
        {
            result = "[" + string.Join(',', partial) + "]";
        }
        else
        {
            var separator = ",\n" + state.Indent;
            var properties = string.Join(separator, partial);
            result = "[\n" + state.Indent + properties + "\n" + stepback + "]";
        }

        state.Indent = stepback;
        state.Stack.Remove(arr);
        return result;
    }

    /// <summary>
    /// Implements QuoteJSONString (ECMA-262 25.5.2.3).
    /// Uses System.Text.Json.JsonSerializer for proper JSON string escaping.
    /// </summary>
    private static string QuoteString(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// Formats a number for JSON output per spec. Handles -0 as "0".
    /// </summary>
    private static string FormatNumber(double d)
    {
        // Per spec, -0 serializes as "0"
        if (d == 0.0 && double.IsNegative(d))
        {
            return "0";
        }

        return d.ToString(CultureInfo.InvariantCulture);
    }

    #endregion

    #region Legacy StringifyValue (kept for backward compatibility)

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

    #endregion
}

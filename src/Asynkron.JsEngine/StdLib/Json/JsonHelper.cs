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
    /// Tracks source text for the json-parse-with-source feature.
    /// Maps (holder identity, property key) -> source text.
    /// </summary>
    private sealed class SourceTracker
    {
        // Track (holder reference, key) -> (source text, original parsed value)
        // The original value is used to verify the value wasn't replaced by the reviver.
        internal readonly Dictionary<(int holderHash, string key), (string Source, JsValue OriginalValue)> Sources = new();

        internal void Track(object holder, string key, string source, JsValue originalValue)
        {
            Sources[(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(holder), key)] = (source, originalValue);
        }

        internal string? GetSource(object holder, string key, JsValue currentValue)
        {
            if (!Sources.TryGetValue(
                    (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(holder), key), out var entry))
            {
                return null;
            }

            // Only return source if the current value matches the original parsed value
            // This handles the case where the reviver modifies the value
            if (!JsOps.SameValue(currentValue, entry.OriginalValue))
            {
                return null;
            }

            return entry.Source;
        }
    }

    /// <summary>
    /// JsValue-returning overload for ParseJsonWithReviver that avoids boxing.
    /// Implements ECMA-262 25.5.1 JSON.parse ( text [ , reviver ] ).
    /// </summary>
    internal static JsValue ParseJsonWithReviverJsValue(string jsonStr, RealmState realm, EvaluationContext? context,
        JsValue reviverValue)
    {
        JsonDocument jsonDoc;
        try
        {
            jsonDoc = JsonDocument.Parse(jsonStr);
        }
        catch
        {
            throw ThrowSyntaxError("Unexpected token in JSON", context, realm);
        }

        if (!reviverValue.TryGetObject<IJsCallable>(out var reviver))
        {
            return ParseJsonValue(jsonDoc.RootElement, realm, null, null, null);
        }

        // When there's a reviver, we need to track source text for the json-parse-with-source feature
        var sourceTracker = new SourceTracker();

        // Step 7a: Let root be OrdinaryObjectCreate(%Object.prototype%).
        var root = new JsObject(realm.ObjectPrototype) { RealmState = realm };

        var parsed = ParseJsonValue(jsonDoc.RootElement, realm, root, "", sourceTracker);

        // Step 7c: Perform ! CreateDataPropertyOrThrow(root, rootName, unfiltered).
        root.DefineProperty("", new PropertyDescriptor
        {
            Value = parsed,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        // Step 7e: Return ? InternalizeJSONProperty(root, rootName, reviver).
        return InternalizeJsonProperty(root, "", reviver, realm, sourceTracker);
    }

    private static JsValue ParseJsonValue(JsonElement element, RealmState realm,
        object? parentHolder, string? parentKey, SourceTracker? tracker)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsObject(realm.ObjectPrototype) { RealmState = realm };
                foreach (var prop in element.EnumerateObject())
                {
                    // Use DefineProperty to avoid __proto__ setter behavior
                    var propValue = ParseJsonValue(prop.Value, realm, obj, prop.Name, tracker);
                    obj.DefineProperty(prop.Name, new PropertyDescriptor
                    {
                        Value = propValue,
                        Writable = true,
                        Enumerable = true,
                        Configurable = true
                    });
                }

                return new JsValue(obj);

            case JsonValueKind.Array:
                var arr = new JsArray(realm);
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var itemValue = ParseJsonValue(item, realm, arr, index.ToString(CultureInfo.InvariantCulture),
                        tracker);
                    arr.Push(itemValue);
                    index++;
                }

                return JsValue.FromJsArray(arr);

            case JsonValueKind.String:
            {
                var result = new JsValue(element.GetString() ?? string.Empty);
                if (tracker is not null && parentHolder is not null && parentKey is not null)
                {
                    tracker.Track(parentHolder, parentKey, element.GetRawText(), result);
                }

                return result;
            }

            case JsonValueKind.Number:
            {
                var rawText = element.GetRawText();
                var number = element.GetDouble();
                var result = new JsValue(number == 0.0d && rawText.Length > 0 && rawText[0] == '-'
                    ? -0.0d
                    : number);
                if (tracker is not null && parentHolder is not null && parentKey is not null)
                {
                    tracker.Track(parentHolder, parentKey, rawText, result);
                }

                return result;
            }

            case JsonValueKind.True:
                if (tracker is not null && parentHolder is not null && parentKey is not null)
                {
                    tracker.Track(parentHolder, parentKey, "true", JsValue.True);
                }

                return JsValue.True;

            case JsonValueKind.False:
                if (tracker is not null && parentHolder is not null && parentKey is not null)
                {
                    tracker.Track(parentHolder, parentKey, "false", JsValue.False);
                }

                return JsValue.False;

            case JsonValueKind.Null:
            default:
                if (tracker is not null && parentHolder is not null && parentKey is not null)
                {
                    tracker.Track(parentHolder, parentKey, "null", JsValue.Null);
                }

                return JsValue.Null;
        }
    }

    /// <summary>
    /// Implements InternalizeJSONProperty (ECMA-262 25.5.1.1) with json-parse-with-source support.
    /// </summary>
    private static JsValue InternalizeJsonProperty(IJsPropertyAccessor holder, string name, IJsCallable reviver,
        RealmState realm, SourceTracker? sourceTracker)
    {
        // Step 1: Let val be ? Get(holder, name).
        holder.TryGetProperty(name, out var val);

        // Step 2: If val is an Object, then
        if (val.IsObject)
        {
            // Step 2a: Let isArray be ? IsArray(val).
            var isArray = StandardLibrary.ArrayIsArray(val, realm);

            if (isArray)
            {
                // Step 2b: If isArray is true, then
                if (!val.TryGetObject<IJsPropertyAccessor>(out var arrAccessor))
                {
                    arrAccessor = (IJsPropertyAccessor)val.ObjectValue!;
                }

                var len = StandardLibrary.LengthOfArrayLike(arrAccessor, realm);

                for (long i = 0; i < len; i++)
                {
                    var prop = i.ToString(CultureInfo.InvariantCulture);
                    var newElement = InternalizeJsonProperty(arrAccessor, prop, reviver, realm, sourceTracker);

                    if (newElement.IsUndefined)
                    {
                        if (arrAccessor is IJsObjectLike objLike)
                        {
                            objLike.Delete(prop);
                        }
                    }
                    else
                    {
                        CreateDataProperty(arrAccessor, prop, newElement);
                    }
                }
            }
            else
            {
                // Step 2c: Else (val is an Object but not an array)
                var keys = GetEnumerableOwnPropertyNames(val);

                foreach (var p in keys)
                {
                    if (!val.TryGetObject<IJsPropertyAccessor>(out var objAccessor))
                    {
                        break;
                    }

                    var newElement = InternalizeJsonProperty(objAccessor, p, reviver, realm, sourceTracker);

                    if (newElement.IsUndefined)
                    {
                        if (objAccessor is IJsObjectLike objLike)
                        {
                            objLike.Delete(p);
                        }
                    }
                    else
                    {
                        CreateDataProperty(objAccessor, p, newElement);
                    }
                }
            }
        }

        // Re-read val after potential modifications by recursive InternalizeJSONProperty calls
        holder.TryGetProperty(name, out val);

        // Build the context argument for json-parse-with-source
        var context = new JsObject(realm.ObjectPrototype) { RealmState = realm };

        // Look up source text: only for values that are still the original parsed primitive
        string? sourceText = null;
        if (sourceTracker is not null)
        {
            sourceText = sourceTracker.GetSource(holder, name, val);
        }

        if (sourceText is not null)
        {
            // Primitive value: context has a "source" property
            context.DefineProperty("source", new PropertyDescriptor
            {
                Value = new JsValue(sourceText),
                Writable = true,
                Enumerable = true,
                Configurable = true
            });
        }
        // For objects/arrays: context has no properties (empty object)

        // Step 3: Return ? Call(reviver, holder, << name, val, context >>).
        return reviver.Invoke([new JsValue(name), val, new JsValue(context)], JsValue.FromObjectUnsafe(holder));
    }

    /// <summary>
    /// Performs CreateDataProperty on a target.
    /// </summary>
    private static void CreateDataProperty(IJsPropertyAccessor target, string key, JsValue value)
    {
        var desc = new PropertyDescriptor
        {
            Value = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        };

        if (target is IPropertyDefinitionHost host)
        {
            host.TryDefineProperty(key, desc);
        }
        else if (target is IJsObjectLike objLike)
        {
            try
            {
                objLike.DefineProperty(key, desc);
            }
            catch (ThrowSignal)
            {
                // DefineProperty may throw for non-configurable properties;
                // InternalizeJSONProperty ignores such failures
            }
        }
        else
        {
            target.SetProperty(key, value);
        }
    }

    /// <summary>
    /// Gets enumerable own property names of a value, handling Proxy and regular objects.
    /// </summary>
    private static List<string> GetEnumerableOwnPropertyNames(JsValue val)
    {
        var keys = new List<string>();

        if (val.TryGetObject<JsProxy>(out var proxy))
        {
            foreach (var key in proxy.GetOwnPropertyKeysInOrder(includeSymbols: false, includeNonEnumerable: false))
            {
                keys.Add(key);
            }
        }
        else if (val.TryGetObject<JsObject>(out var jsObj))
        {
            foreach (var key in jsObj.GetOwnEnumerablePropertyKeysInOrder(false))
            {
                keys.Add(key);
            }
        }
        else if (val.TryGetObject<IJsObjectLike>(out var objLike))
        {
            foreach (var key in objLike.Keys)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    #region JSON.stringify (ECMA-262 25.5.2)

    private sealed class StringifyState
    {
        internal IJsCallable? ReplacerFunction;
        internal List<string>? PropertyList;
        internal string Gap = string.Empty;
        internal string Indent = string.Empty;
        internal readonly HashSet<object> Stack = new(ReferenceEqualityComparer.Instance);
        internal RealmState? Realm;
    }

    internal static JsValue Stringify(JsValue value, JsValue replacerArg, JsValue spaceArg, RealmState? realm = null)
    {
        var state = new StringifyState { Realm = realm };

        // Step 4: Process replacer
        // Per spec: If IsCallable(replacer) is true, set ReplacerFunction.
        // Note: JsProxy always implements IJsCallable, but is only callable if target is callable.
        if (replacerArg.TryGetObject<IJsCallable>(out var replacerFn) &&
            (replacerFn is not JsProxy replacerProxy || replacerProxy.IsCallableTarget()))
        {
            state.ReplacerFunction = replacerFn;
        }
        else if (IsArrayForReplacer(replacerArg, realm))
        {
            state.PropertyList = BuildPropertyList(replacerArg, realm);
        }

        // Step 5-8: Process space
        // Per spec: If space has [[NumberData]], set space to ToNumber(space).
        // If space has [[StringData]], set space to ToString(space).
        var space = spaceArg;
        if (space.IsObject && space.TryGetObject<JsObject>(out var spaceObj) &&
            spaceObj.TryGetProperty("__value__", out var spaceInner))
        {
            if (spaceInner.IsNumber)
            {
                space = new JsValue(JsOps.ToNumber(space));
            }
            else if (spaceInner.IsString)
            {
                space = new JsValue(JsOps.ToJsString(space));
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

        // Step 9: Let wrapper be OrdinaryObjectCreate(%Object.prototype%).
        // Step 10: Perform ! CreateDataPropertyOrThrow(wrapper, "", value).
        var wrapper = realm is not null
            ? new JsObject(realm.ObjectPrototype) { RealmState = realm }
            : new JsObject();
        wrapper.DefineProperty("", new PropertyDescriptor
        {
            Value = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        // Step 10: Call SerializeJSONProperty with the wrapper
        var result = SerializeJsonProperty(state, "", wrapper);

        return result is null ? JsValue.Undefined : new JsValue(result);
    }

    private static bool IsArrayForReplacer(JsValue replacerArg, RealmState? realm)
    {
        if (replacerArg.TryGetObject<JsArray>(out _))
        {
            return true;
        }

        return StandardLibrary.ArrayIsArray(replacerArg, realm);
    }

    private static List<string> BuildPropertyList(JsValue replacerArg, RealmState? realm)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!replacerArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return list;
        }

        var length = StandardLibrary.LengthOfArrayLike(accessor, realm);

        for (long i = 0; i < length; i++)
        {
            var indexStr = i.ToString(CultureInfo.InvariantCulture);
            if (!accessor.TryGetProperty(indexStr, out var item))
            {
                continue;
            }

            string? itemStr = null;

            if (item.IsString)
            {
                itemStr = item.AsString();
            }
            else if (item.IsNumber)
            {
                itemStr = JsOps.ToCanonicalNumberString(item.NumberValue);
            }
            else if (item.IsObject && item.TryGetObject<JsObject>(out var obj) &&
                     obj.TryGetProperty("__value__", out var inner))
            {
                // Per spec step 4.d.iii: If v has [[StringData]] or [[NumberData]],
                // set item to ? ToString(v).
                if (inner.IsString || inner.IsNumber)
                {
                    itemStr = JsOps.ToJsString(item);
                }
            }

            if (itemStr is not null && seen.Add(itemStr))
            {
                list.Add(itemStr);
            }
        }

        return list;
    }

    private static string? SerializeJsonProperty(StringifyState state, string key, IJsPropertyAccessor holder)
    {
        holder.TryGetProperty(key, out var value);

        // Step 2: If Type(value) is Object or BigInt, check toJSON
        if (value.IsObject && value.TryGetObject<IJsPropertyAccessor>(out var toJsonHolder))
        {
            if (toJsonHolder.TryGetProperty("toJSON", out var toJson) &&
                toJson.TryGetObject<IJsCallable>(out var toJsonFn))
            {
                value = toJsonFn.Invoke([new JsValue(key)], value);
            }
        }
        else if (value.IsBigInt)
        {
            // Per spec: GetV(value, "toJSON") - look up toJSON on BigInt.prototype
            // with the BigInt value as receiver so getters see the correct `this`
            if (state.Realm?.BigIntPrototype is { } bigIntProto &&
                bigIntProto.TryGetProperty("toJSON", value, out var bigIntToJson) &&
                bigIntToJson.TryGetObject<IJsCallable>(out var bigIntToJsonFn))
            {
                value = bigIntToJsonFn.Invoke([new JsValue(key)], value);
            }
        }

        // Step 3: Replacer function
        if (state.ReplacerFunction is not null)
        {
            value = state.ReplacerFunction.Invoke(
                [new JsValue(key), value],
                JsValue.FromObjectUnsafe((object)holder));
        }

        // Check for rawJSON marker (JSON.rawJSON objects) - AFTER toJSON and replacer
        if (value.IsObject && value.TryGetObject<JsObject>(out var rawJsonCheck))
        {
            if (string.Equals(rawJsonCheck.Origin, "[[IsRawJSON]]", StringComparison.Ordinal) &&
                rawJsonCheck.TryGetProperty("rawJSON", out var rawText) && rawText.IsString)
            {
                return rawText.AsString()!;
            }
        }

        // Step 4: Unwrap wrapper objects per spec:
        // If value has [[NumberData]], set value to ToNumber(value).
        // If value has [[StringData]], set value to ToString(value).
        // If value has [[BooleanData]], set value to value.[[BooleanData]].
        // If value has [[BigIntData]], set value to value.[[BigIntData]].
        if (value.IsObject && value.TryGetObject<JsObject>(out var wrapperObj))
        {
            if (wrapperObj.TryGetProperty("__value__", out var innerVal))
            {
                if (innerVal.IsNumber)
                {
                    value = new JsValue(JsOps.ToNumber(value));
                }
                else if (innerVal.IsString)
                {
                    value = new JsValue(JsOps.ToJsString(value));
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

        if (value.IsBigInt)
        {
            throw ThrowTypeError("Do not know how to serialize a BigInt");
        }

        if (value.IsUndefined || value.IsSymbol)
        {
            return null;
        }

        if (value.IsObject && value.TryGetObject<IJsCallable>(out var callableCheck))
        {
            // JsProxy always implements IJsCallable but is only actually callable if target is callable.
            // Skip the callable check for non-callable proxies.
            if (callableCheck is not JsProxy proxyCheck || proxyCheck.IsCallableTarget())
            {
                return null;
            }
        }

        // Arrays and Objects - use IsArray for Proxy support
        if (StandardLibrary.ArrayIsArray(value, state.Realm))
        {
            return SerializeJsonArray(state, value);
        }

        if (value.TryGetObject<IJsPropertyAccessor>(out var objAccessor))
        {
            return SerializeJsonObject(state, objAccessor);
        }

        return null;
    }

    private static string SerializeJsonObject(StringifyState state, IJsPropertyAccessor obj)
    {
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
            // Per spec: Let K be ? EnumerableOwnPropertyNames(value, key).
            keys = [];
            if (obj is JsProxy proxy)
            {
                foreach (var k in proxy.GetOwnPropertyKeysInOrder(includeSymbols: false,
                             includeNonEnumerable: false))
                {
                    keys.Add(k);
                }
            }
            else if (obj is JsObject jsObj)
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

    private static string SerializeJsonArray(StringifyState state, JsValue arrayValue)
    {
        if (!arrayValue.TryGetObject<IJsPropertyAccessor>(out var arr))
        {
            return "[]";
        }

        if (!state.Stack.Add(arr))
        {
            throw ThrowTypeError("Converting circular structure to JSON");
        }

        var stepback = state.Indent;
        state.Indent = stepback + state.Gap;

        var partial = new List<string>();
        var length = StandardLibrary.LengthOfArrayLike(arr, state.Realm);

        for (long index = 0; index < length; index++)
        {
            var strP = SerializeJsonProperty(state, index.ToString(CultureInfo.InvariantCulture), arr);
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
    /// Implements QuoteJSONString per ECMA-262 25.5.2.3.
    /// Uses lowercase hex for unicode escapes per spec requirement.
    /// </summary>
    private static string QuoteString(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 2);
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (char.IsHighSurrogate(c))
                    {
                        // Check if next char is a valid low surrogate (valid pair)
                        if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            // Valid surrogate pair - emit both chars as-is
                            sb.Append(c);
                            sb.Append(value[++i]);
                        }
                        else
                        {
                            // Lone high surrogate - must escape
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        // Lone low surrogate - must escape
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string FormatNumber(double d)
    {
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
                return "null";
            }

            switch (value)
            {
                case null:
                    return "null";

                case JsValue jsValue:
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

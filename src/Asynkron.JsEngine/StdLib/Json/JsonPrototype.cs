#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.JsonHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("JSON", ToStringTag = "JSON", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class JsonPrototype
{
    private const string RawJsonOriginMarker = "[[IsRawJSON]]";

    [JsHostMethod("parse", Length = 2d)]
    public JsValue Parse(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowSyntaxError("Unexpected end of JSON input", realm: Realm);
        }

        // Step 1: Let jsonString be ? ToString(text).
        // Use no context so that ToPrimitive exceptions propagate as ThrowSignals directly.
        var jsonStr = JsOps.ToJsString(args[0]);
        var reviver = args.GetArgument(1);
        var context = Realm.CreateContext();
        return ParseJsonWithReviverJsValue(jsonStr, Realm, context, reviver);
    }

    [JsHostMethod("stringify", Length = 3d)]
    public JsValue Stringify(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var value = args[0];
        var replacer = args.GetArgument(1);
        var space = args.GetArgument(2);

        return JsonHelper.Stringify(value, replacer, space, Realm);
    }

    /// <summary>
    /// JSON.rawJSON(text) -- creates a raw JSON marker object that JSON.stringify
    /// will emit without quoting or escaping.
    /// Per spec: OrdinaryObjectCreate(null) with rawJSON property.
    /// </summary>
    [JsHostMethod("rawJSON", Length = 1d)]
    public JsValue RawJSON(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("JSON.rawJSON requires a value", realm: Realm);
        }

        var text = JsOps.ToJsString(args[0]);

        // Step 2: Throw SyntaxError if empty, or starts/ends with whitespace
        if (string.IsNullOrEmpty(text))
        {
            throw ThrowSyntaxError("JSON.rawJSON: empty string is not valid JSON", realm: Realm);
        }

        var first = text[0];
        var last = text[text.Length - 1];
        if (first is '\t' or '\n' or '\r' or ' ' ||
            last is '\t' or '\n' or '\r' or ' ')
        {
            throw ThrowSyntaxError("JSON.rawJSON: text must not start or end with whitespace", realm: Realm);
        }

        // Validate by attempting to parse -- must be a single JSON value
        try
        {
            System.Text.Json.JsonDocument.Parse(text);
        }
        catch
        {
            throw ThrowSyntaxError($"JSON.rawJSON: invalid JSON text: {text}", realm: Realm);
        }

        // Step 5: Let obj be OrdinaryObjectCreate(null, internalSlotsList).
        // null prototype per spec
        var obj = new JsObject(); // no prototype argument = null prototype
        // Step 6: Perform ! CreateDataPropertyOrThrow(obj, "rawJSON", jsonString).
        obj.DefineProperty("rawJSON", new PropertyDescriptor
        {
            Value = new JsValue(text),
            Writable = false,
            Enumerable = true,
            Configurable = false
        });
        // Mark as raw JSON via the Origin field (not a JS-visible property)
        obj.Origin = RawJsonOriginMarker;
        // Step 8: Return obj. (Frozen/non-extensible per spec step 7: Perform ! SetIntegrityLevel(obj, frozen))
        obj.PreventExtensions();
        return JsValue.FromObjectUnsafe(obj);
    }

    /// <summary>
    /// JSON.isRawJSON(value) -- checks if a value is a raw JSON marker object.
    /// </summary>
    [JsHostMethod("isRawJSON", Length = 1d)]
    public JsValue IsRawJSON(IReadOnlyList<JsValue> args)
    {
        var value = args.GetArgument(0);
        if (value.TryGetObject<JsObject>(out var obj) &&
            string.Equals(obj.Origin, RawJsonOriginMarker, StringComparison.Ordinal))
        {
            return JsValue.True;
        }

        return JsValue.False;
    }
}

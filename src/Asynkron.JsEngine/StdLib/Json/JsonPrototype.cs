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
    [JsHostMethod("parse", Length = 2d)]
    public JsValue Parse(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowSyntaxError("Unexpected end of JSON input", realm: Realm);
        }

        var context = Realm.CreateContext();
        var jsonStr = JsOps.ToJsString(args[0], context);
        var reviver = args.GetArgument(1);
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

        return JsonHelper.Stringify(value, replacer, space);
    }

    /// <summary>
    /// JSON.rawJSON(text) — creates a raw JSON marker object that JSON.stringify
    /// will emit without quoting or escaping.
    /// </summary>
    [JsHostMethod("rawJSON", Length = 1d)]
    public JsValue RawJSON(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("JSON.rawJSON requires a value", realm: Realm);
        }

        var text = JsOps.ToJsString(args[0]);

        // Validate: must be valid JSON text and not 'null', 'true', 'false', or a string
        // Actually per spec, the text just needs to be valid JSON value text
        if (string.IsNullOrEmpty(text))
        {
            throw ThrowSyntaxError("JSON.rawJSON: empty string is not valid JSON", realm: Realm);
        }

        // Validate by attempting to parse — must be a single JSON value
        try
        {
            System.Text.Json.JsonDocument.Parse(text);
        }
        catch
        {
            throw ThrowSyntaxError($"JSON.rawJSON: invalid JSON text: {text}", realm: Realm);
        }

        // Create a frozen object with null prototype and [[IsRawJSON]] internal slot
        var obj = new JsObject(); // null prototype
        obj.DefineProperty("rawJSON", new PropertyDescriptor
        {
            Value = new JsValue(text),
            Writable = false,
            Enumerable = true,
            Configurable = false
        });
        // Mark as raw JSON via internal slot
        obj.DefineProperty("[[IsRawJSON]]", new PropertyDescriptor
        {
            Value = JsValue.True,
            Writable = false,
            Enumerable = false,
            Configurable = false
        });
        // Freeze the object
        obj.PreventExtensions();
        return JsValue.FromObjectUnsafe(obj);
    }

    /// <summary>
    /// JSON.isRawJSON(value) — checks if a value is a raw JSON marker object.
    /// </summary>
    [JsHostMethod("isRawJSON", Length = 1d)]
    public JsValue IsRawJSON(IReadOnlyList<JsValue> args)
    {
        var value = args.GetArgument(0);
        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("[[IsRawJSON]]", out var marker) &&
            marker.IsBoolean && marker.NumberValue != 0)
        {
            return JsValue.True;
        }

        return JsValue.False;
    }
}

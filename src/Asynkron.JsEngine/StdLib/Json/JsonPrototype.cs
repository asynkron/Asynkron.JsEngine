using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("JSON", ToStringTag = "JSON", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class JsonPrototype : JsPrototype
{
    [JsHostMethod("parse", Length = 2d)]
    public object? Parse(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowSyntaxError("Unexpected end of JSON input", realm: Realm);
        }

        var context = Realm.CreateContext();
        var jsonStr = JsOps.ToJsString(args[0], context);
        var reviver = args.GetArgument(1);
        return ParseJsonWithReviver(jsonStr, Realm, context, reviver);
    }

    [JsHostMethod("stringify", Length = 3d)]
    public object? Stringify(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "undefined";
        }

        var value = args[0];

        // TODO: replacer and space are not yet supported; fallback to basic stringify.
        return StringifyValue(value);
    }
}

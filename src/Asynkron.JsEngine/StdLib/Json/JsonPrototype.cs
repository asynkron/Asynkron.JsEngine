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
}

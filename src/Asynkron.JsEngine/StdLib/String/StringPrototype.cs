using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("String", ToStringTag = "String")]
public sealed partial class StringPrototype : JsPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public object? ToString(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireStringReceiver(thisValue, Realm);
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public object? ValueOf(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireStringReceiver(thisValue, Realm);
    }

    [JsHostMethod("parseJSON", Length = 1d)]
    public object? ParseJson(object? thisValue, IReadOnlyList<object?> args)
    {
        var context = Realm.CreateContext();
        var source = JsOps.ToJsString(thisValue, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var reviver = args.Count > 0 ? args[0] : null;
        return ParseJsonWithReviver(source, Realm, context, reviver);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject stringProto)
        {
            Realm.StringPrototype ??= stringProto;
            stringProto.DefineProperty("length",
                new PropertyDescriptor
                {
                    Value = 0d,
                    Writable = false,
                    Enumerable = false,
                    Configurable = false,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });

            AddStringMethods(stringProto, Realm);
        }
    }
}

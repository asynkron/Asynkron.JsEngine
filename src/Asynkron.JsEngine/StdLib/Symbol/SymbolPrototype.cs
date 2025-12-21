using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Symbol", ToStringTag = "Symbol")]
public sealed partial class SymbolPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(RequireSymbolReceiver(thisValue, Realm).ToString());
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var symbol = RequireSymbolReceiver(thisValue, Realm);
        return new JsValue(JsValueKind.Symbol, 0.0, symbol);
    }

    [JsHostGetter("description", Configurable = true)]
    public JsValue Description(JsValue thisValue)
    {
        var symbol = RequireSymbolReceiver(thisValue, Realm);
        return symbol.Description != null ? new JsValue(symbol.Description) : JsValue.Undefined;
    }

    protected override void ConfigurePrototype()
    {
        var toPrimitiveKey = SymbolKeys.ToPrimitive;
        Prototype.SetProperty(toPrimitiveKey,
            new HostFunction((thisValue, _) =>
            {
                var symbol = RequireSymbolReceiver(thisValue, Realm);
                return new JsValue(JsValueKind.Symbol, 0.0, symbol);
            }, Realm, isConstructor: false));

        var toStringTagKey = SymbolKeys.ToStringTag;
        Prototype.SetProperty(toStringTagKey, "Symbol");
    }
}

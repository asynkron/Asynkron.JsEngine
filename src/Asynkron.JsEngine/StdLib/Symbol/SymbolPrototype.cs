using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Symbol", ToStringTag = "Symbol")]
public sealed partial class SymbolPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public object? ToString(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireSymbolReceiver(thisValue, Realm).ToString();
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public object? ValueOf(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireSymbolReceiver(thisValue, Realm);
    }

    [JsHostGetter("description", Configurable = true)]
    public object? Description(object? thisValue)
    {
        var symbol = RequireSymbolReceiver(thisValue, Realm);
        return symbol.Description ?? (object)Symbol.Undefined;
    }

    protected override void ConfigurePrototype()
    {
        var toPrimitiveKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toPrimitive").GetHashCode()}";
        Prototype.SetProperty(toPrimitiveKey,
            new HostFunction((thisValue, _) => RequireSymbolReceiver(thisValue, Realm), Realm, isConstructor: false));

        var toStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        Prototype.SetProperty(toStringTagKey, "Symbol");
    }
}

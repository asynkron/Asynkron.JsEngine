#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Symbol", PrototypeType = typeof(SymbolPrototype), Length = 0d, DisplayName = "Symbol")]
public sealed partial class SymbolConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("Symbol is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        Realm.SymbolPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) => newTarget.IsUndefined ? CreateSymbolValue(args) : throw ThrowTypeError("Symbol is not a constructor", realm: Realm));

        AttachStatics(constructor);
    }

    private static JsValue CreateSymbolValue(IReadOnlyList<JsValue> args)
    {
        var description = args.Count > 0 && !args[0].IsUndefined
            ? args[0].ToJsString()
            : null;
        return new JsValue(JsValueKind.Symbol, 0.0, TypedAstSymbol.Create(description));
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("for", new HostFunction(SymbolFor, Realm, false), Realm);
        constructor.SetHostedProperty("keyFor", new HostFunction(SymbolKeyFor, Realm, false), Realm);

        constructor.SetProperty("hasInstance", (JsValue)Symbols.HasInstance);
        constructor.SetProperty("iterator", (JsValue)Symbols.Iterator);
        constructor.SetProperty("asyncIterator", (JsValue)Symbols.AsyncIterator);
        constructor.SetProperty("toPrimitive", (JsValue)Symbols.ToPrimitive);
        constructor.SetProperty("toStringTag", (JsValue)Symbols.ToStringTag);
        constructor.SetProperty("unscopables", (JsValue)Symbols.Unscopables);
        constructor.SetProperty("match", (JsValue)Symbols.Match);
        constructor.SetProperty("matchAll", (JsValue)Symbols.MatchAll);
        constructor.SetProperty("replace", (JsValue)Symbols.Replace);
        constructor.SetProperty("replaceAll", (JsValue)Symbols.ReplaceAll);
        constructor.SetProperty("search", (JsValue)Symbols.Search);
        constructor.SetProperty("split", (JsValue)Symbols.Split);
        constructor.SetProperty("species", (JsValue)Symbols.Species);
        constructor.SetProperty("isConcatSpreadable", (JsValue)Symbols.IsConcatSpreadable);
    }

    private static JsValue SymbolFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var key = args[0].ToJsString();
        return new JsValue(JsValueKind.Symbol, 0.0, TypedAstSymbol.For(key));
    }

    private static JsValue SymbolKeyFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].IsSymbol || args[0].ObjectValue is not TypedAstSymbol sym)
        {
            return JsValue.Undefined;
        }

        var key = TypedAstSymbol.KeyFor(sym);
        return key != null ? new JsValue(key) : JsValue.Undefined;
    }
}

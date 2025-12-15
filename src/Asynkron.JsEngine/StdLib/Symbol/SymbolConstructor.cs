using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Symbol", PrototypeType = typeof(SymbolPrototype), Length = 0d, DisplayName = "Symbol")]
public sealed partial class SymbolConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("Symbol is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.SymbolPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.IsUndefined)
            {
                throw ThrowTypeError("Symbol is not a constructor", realm: Realm);
            }

            return CreateSymbolValue(args);
        });

        AttachStatics(constructor);
    }

    private JsValue CreateSymbolValue(IReadOnlyList<JsValue> args)
    {
        var description = args.Count > 0 && !args[0].IsUndefined
            ? args[0].ToString()
            : null;
        return new JsValue(JsValueKind.Symbol, 0.0, TypedAstSymbol.Create(description));
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("for", new HostFunction(SymbolFor, Realm, isConstructor: false), Realm);
        constructor.SetHostedProperty("keyFor", new HostFunction(SymbolKeyFor, Realm, isConstructor: false), Realm);

        constructor.SetProperty("hasInstance", JsValue.FromObject(Symbols.HasInstance));
        constructor.SetProperty("iterator", JsValue.FromObject(Symbols.Iterator));
        constructor.SetProperty("asyncIterator", JsValue.FromObject(Symbols.AsyncIterator));
        constructor.SetProperty("toPrimitive", JsValue.FromObject(Symbols.ToPrimitive));
        constructor.SetProperty("toStringTag", JsValue.FromObject(Symbols.ToStringTag));
        constructor.SetProperty("unscopables", JsValue.FromObject(Symbols.Unscopables));
        constructor.SetProperty("match", JsValue.FromObject(Symbols.Match));
        constructor.SetProperty("matchAll", JsValue.FromObject(Symbols.MatchAll));
        constructor.SetProperty("replace", JsValue.FromObject(Symbols.Replace));
        constructor.SetProperty("replaceAll", JsValue.FromObject(Symbols.ReplaceAll));
        constructor.SetProperty("search", JsValue.FromObject(Symbols.Search));
        constructor.SetProperty("split", JsValue.FromObject(Symbols.Split));
        constructor.SetProperty("species", JsValue.FromObject(Symbols.Species));
        constructor.SetProperty("isConcatSpreadable", JsValue.FromObject(Symbols.IsConcatSpreadable));
    }

    private JsValue SymbolFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var key = JsOps.ToJsString(args[0].ToObject());
        return new JsValue(JsValueKind.Symbol, 0.0, TypedAstSymbol.For(key));
    }

    private JsValue SymbolKeyFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].IsSymbol || args[0].ObjectValue is not TypedAstSymbol sym)
        {
            return JsValue.Undefined;
        }

        var key = TypedAstSymbol.KeyFor(sym);
        return key != null ? new JsValue(key) : JsValue.Undefined;
    }
}

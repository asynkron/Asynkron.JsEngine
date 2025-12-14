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
            if (newTarget is not null)
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
        return new JsValue(TypedAstSymbol.Create(description));
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("for", new HostFunction(SymbolFor, Realm, isConstructor: false), Realm);
        constructor.SetHostedProperty("keyFor", new HostFunction(SymbolKeyFor, Realm, isConstructor: false), Realm);

        constructor.SetProperty("hasInstance", Symbols.HasInstance);
        constructor.SetProperty("iterator", Symbols.Iterator);
        constructor.SetProperty("asyncIterator", Symbols.AsyncIterator);
        constructor.SetProperty("toPrimitive", Symbols.ToPrimitive);
        constructor.SetProperty("toStringTag", Symbols.ToStringTag);
        constructor.SetProperty("unscopables", Symbols.Unscopables);
        constructor.SetProperty("match", Symbols.Match);
        constructor.SetProperty("matchAll", Symbols.MatchAll);
        constructor.SetProperty("replace", Symbols.Replace);
        constructor.SetProperty("replaceAll", Symbols.ReplaceAll);
        constructor.SetProperty("search", Symbols.Search);
        constructor.SetProperty("split", Symbols.Split);
        constructor.SetProperty("species", Symbols.Species);
        constructor.SetProperty("isConcatSpreadable", Symbols.IsConcatSpreadable);
    }

    private JsValue SymbolFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var key = args[0].ToString() ?? "";
        return new JsValue(TypedAstSymbol.For(key));
    }

    private JsValue SymbolKeyFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !(args[0].IsObject && args[0].AsObject() is TypedAstSymbol sym))
        {
            return JsValue.Undefined;
        }

        var key = TypedAstSymbol.KeyFor(sym);
        return key != null ? new JsValue(key) : JsValue.Undefined;
    }
}

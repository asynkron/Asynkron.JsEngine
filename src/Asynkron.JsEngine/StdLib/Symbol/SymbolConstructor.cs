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

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
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

    private object CreateSymbolValue(IReadOnlyList<object?> args)
    {
        var description = args.Count > 0 && args[0] != null && !ReferenceEquals(args[0], Symbol.Undefined)
            ? args[0]!.ToString()
            : null;
        return TypedAstSymbol.Create(description);
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("for", SymbolFor);
        constructor.SetHostedProperty("keyFor", SymbolKeyFor);

        constructor.SetProperty("hasInstance", TypedAstSymbol.For("Symbol.hasInstance"));
        constructor.SetProperty("iterator", TypedAstSymbol.For("Symbol.iterator"));
        constructor.SetProperty("asyncIterator", TypedAstSymbol.For("Symbol.asyncIterator"));
        constructor.SetProperty("toPrimitive", TypedAstSymbol.For("Symbol.toPrimitive"));
        constructor.SetProperty("toStringTag", TypedAstSymbol.For("Symbol.toStringTag"));
        constructor.SetProperty("unscopables", TypedAstSymbol.For("Symbol.unscopables"));
        constructor.SetProperty("match", TypedAstSymbol.For("Symbol.match"));
        constructor.SetProperty("matchAll", TypedAstSymbol.For("Symbol.matchAll"));
        constructor.SetProperty("replace", TypedAstSymbol.For("Symbol.replace"));
        constructor.SetProperty("replaceAll", TypedAstSymbol.For("Symbol.replaceAll"));
        constructor.SetProperty("search", TypedAstSymbol.For("Symbol.search"));
        constructor.SetProperty("split", TypedAstSymbol.For("Symbol.split"));
        constructor.SetProperty("species", TypedAstSymbol.For("Symbol.species"));
        constructor.SetProperty("isConcatSpreadable", TypedAstSymbol.For("Symbol.isConcatSpreadable"));
    }

    private object? SymbolFor(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return Symbol.Undefined;
        }

        var key = args[0]?.ToString() ?? "";
        return TypedAstSymbol.For(key);
    }

    private object? SymbolKeyFor(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not TypedAstSymbol sym)
        {
            return Symbol.Undefined;
        }

        var key = TypedAstSymbol.KeyFor(sym);
        return key ?? (object)Symbol.Undefined;
    }
}

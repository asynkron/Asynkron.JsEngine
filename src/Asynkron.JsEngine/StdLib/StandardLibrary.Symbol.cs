using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateSymbolConstructor()
    {
        var symbolConstructor = new HostFunction(SymbolConstructor);

        symbolConstructor.SetHostedProperty("for", SymbolFor);

        symbolConstructor.SetHostedProperty("keyFor", SymbolKeyFor);

        // Well-known symbols
        symbolConstructor.SetProperty("hasInstance", TypedAstSymbol.For("Symbol.hasInstance"));
        symbolConstructor.SetProperty("iterator", TypedAstSymbol.For("Symbol.iterator"));
        symbolConstructor.SetProperty("asyncIterator", TypedAstSymbol.For("Symbol.asyncIterator"));
        symbolConstructor.SetProperty("toPrimitive", TypedAstSymbol.For("Symbol.toPrimitive"));
        symbolConstructor.SetProperty("toStringTag", TypedAstSymbol.For("Symbol.toStringTag"));
        symbolConstructor.SetProperty("unscopables", TypedAstSymbol.For("Symbol.unscopables"));
        symbolConstructor.SetProperty("match", TypedAstSymbol.For("Symbol.match"));
        symbolConstructor.SetProperty("matchAll", TypedAstSymbol.For("Symbol.matchAll"));
        symbolConstructor.SetProperty("replace", TypedAstSymbol.For("Symbol.replace"));
        symbolConstructor.SetProperty("replaceAll", TypedAstSymbol.For("Symbol.replaceAll"));
        symbolConstructor.SetProperty("search", TypedAstSymbol.For("Symbol.search"));
        symbolConstructor.SetProperty("split", TypedAstSymbol.For("Symbol.split"));
        symbolConstructor.SetProperty("species", TypedAstSymbol.For("Symbol.species"));

        return symbolConstructor;

        object? SymbolConstructor(IReadOnlyList<object?> args)
        {
            var description = args.Count > 0 && args[0] != null && !ReferenceEquals(args[0], Symbol.Undefined)
                ? args[0]!.ToString()
                : null;
            return TypedAstSymbol.Create(description);
        }

        object? SymbolFor(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return Symbol.Undefined;
            }

            var key = args[0]?.ToString() ?? "";
            return TypedAstSymbol.For(key);
        }

        object? SymbolKeyFor(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not TypedAstSymbol sym)
        {
            return Symbol.Undefined;
        }

        var key = TypedAstSymbol.KeyFor(sym);
        return key ?? (object)Symbol.Undefined;
    }
    }

    public static JsObject CreateSymbolWrapper(TypedAstSymbol symbol, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var wrapper = new JsObject { ["__value__"] = symbol };

        var proto = context?.RealmState?.ObjectPrototype ?? realm?.ObjectPrototype;
        if (proto is not null)
        {
            wrapper.SetPrototype(proto);
        }

        var valueOf = new HostFunction((thisValue, _) => UnboxSymbol(thisValue, context, realm))
        {
            IsConstructor = false
        };

        var toString = new HostFunction((thisValue, _) => UnboxSymbol(thisValue, context, realm).ToString())
        {
            IsConstructor = false
        };

        wrapper.SetHostedProperty("valueOf", valueOf);
        wrapper.SetHostedProperty("toString", toString);

        var toPrimitiveKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toPrimitive").GetHashCode()}";
        wrapper.SetProperty(toPrimitiveKey,
            new HostFunction((thisValue, _) => UnboxSymbol(thisValue, context, realm)) { IsConstructor = false });

        var toStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        wrapper.SetProperty(toStringTagKey, "Symbol");

        return wrapper;

        static TypedAstSymbol UnboxSymbol(object? receiver, EvaluationContext? ctx, RealmState? realmState)
        {
            switch (receiver)
            {
                case TypedAstSymbol s:
                    return s;
                case JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner is TypedAstSymbol sym:
                    return sym;
                default:
                    throw ThrowTypeError("Symbol.prototype valueOf called on incompatible receiver", ctx, realmState);
            }
        }
    }
}

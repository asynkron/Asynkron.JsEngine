using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateSymbolConstructor()
    {
        var symbolConstructor = new HostFunction(SymbolConstructor);

        symbolConstructor.SetHostedProperty("for", SymbolFor);

        symbolConstructor.SetHostedProperty("keyFor", SymbolKeyFor);

        // Well-known symbols
        symbolConstructor.SetProperty("iterator", TypedAstSymbol.For("Symbol.iterator"));
        symbolConstructor.SetProperty("asyncIterator", TypedAstSymbol.For("Symbol.asyncIterator"));
        symbolConstructor.SetProperty("toPrimitive", TypedAstSymbol.For("Symbol.toPrimitive"));
        symbolConstructor.SetProperty("toStringTag", TypedAstSymbol.For("Symbol.toStringTag"));

        return symbolConstructor;

        object? SymbolConstructor(IReadOnlyList<object?> args)
        {
            var description = args.Count > 0 && args[0] != null && !ReferenceEquals(args[0], Symbols.Undefined)
                ? args[0]!.ToString()
                : null;
            return TypedAstSymbol.Create(description);
        }

        object? SymbolFor(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return Symbols.Undefined;
            }

            var key = args[0]?.ToString() ?? "";
            return TypedAstSymbol.For(key);
        }

        object? SymbolKeyFor(IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not TypedAstSymbol sym)
            {
                return Symbols.Undefined;
            }

            var key = TypedAstSymbol.KeyFor(sym);
            return key ?? (object)Symbols.Undefined;
        }
    }
}

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateSymbolConstructor()
    {
        // Symbol cannot be used with 'new' in JavaScript
        var symbolConstructor = new HostFunction(args =>
        {
            var description = args.Count > 0 && args[0] != null && !ReferenceEquals(args[0], Symbols.Undefined)
                ? args[0]!.ToString()
                : null;
            return TypedAstSymbol.Create(description);
        });

        // Symbol.for(key) - creates/retrieves a global symbol
        symbolConstructor.SetProperty("for", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return Symbols.Undefined;
            }

            var key = args[0]?.ToString() ?? "";
            return TypedAstSymbol.For(key);
        }));

        // Symbol.keyFor(symbol) - gets the key for a global symbol
        symbolConstructor.SetProperty("keyFor", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not TypedAstSymbol sym)
            {
                return Symbols.Undefined;
            }

            var key = TypedAstSymbol.KeyFor(sym);
            return key ?? (object)Symbols.Undefined;
        }));

        // Well-known symbols
        symbolConstructor.SetProperty("iterator", TypedAstSymbol.For("Symbol.iterator"));
        symbolConstructor.SetProperty("asyncIterator", TypedAstSymbol.For("Symbol.asyncIterator"));
        symbolConstructor.SetProperty("toPrimitive", TypedAstSymbol.For("Symbol.toPrimitive"));
        symbolConstructor.SetProperty("toStringTag", TypedAstSymbol.For("Symbol.toStringTag"));

        return symbolConstructor;
    }
}

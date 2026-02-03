#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Symbol", PrototypeType = typeof(SymbolPrototype), Length = 0d, DisplayName = "Symbol")]
public sealed partial class SymbolConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    // Static methods registered via code generation

    [JsConstructorMethod("for", Length = 1d)]
    public static JsValue For(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var key = JsOps.ToJsString(args[0]);
        return new JsValue(JsValueKind.Symbol, 0.0, JsSymbol.For(key));
    }

    [JsConstructorMethod("keyFor", Length = 1d)]
    public static JsValue KeyFor(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].IsSymbol || !args[0].TryUnwrap<JsSymbol>(out var sym))
        {
            throw ThrowTypeError("Symbol.keyFor requires a symbol");
        }

        var key = JsSymbol.KeyFor(sym);
        return key != null ? new JsValue(key) : JsValue.Undefined;
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("Symbol is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        Realm.SymbolPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, context, newTarget) => newTarget.IsUndefined
            ? CreateSymbolValue(args, context)
            : throw ThrowTypeError("Symbol is not a constructor", realm: Realm));

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
        AttachWellKnownSymbols(constructor);
    }

    private static JsValue CreateSymbolValue(IReadOnlyList<JsValue> args, EvaluationContext? context)
    {
        var description = args.Count > 0 && !args[0].IsUndefined
            ? JsOps.ToJsString(args[0], context)
            : null;
        return new JsValue(JsValueKind.Symbol, 0.0, JsSymbol.Create(description));
    }

    private static void AttachWellKnownSymbols(HostFunction constructor)
    {
        DefineConstantProperty(constructor, "hasInstance", (JsValue)Symbols.HasInstance);
        DefineConstantProperty(constructor, "iterator", (JsValue)Symbols.Iterator);
        DefineConstantProperty(constructor, "asyncIterator", (JsValue)Symbols.AsyncIterator);
        DefineConstantProperty(constructor, "toPrimitive", (JsValue)Symbols.ToPrimitive);
        DefineConstantProperty(constructor, "toStringTag", (JsValue)Symbols.ToStringTag);
        DefineConstantProperty(constructor, "unscopables", (JsValue)Symbols.Unscopables);
        DefineConstantProperty(constructor, "match", (JsValue)Symbols.Match);
        DefineConstantProperty(constructor, "matchAll", (JsValue)Symbols.MatchAll);
        DefineConstantProperty(constructor, "replace", (JsValue)Symbols.Replace);
        DefineConstantProperty(constructor, "replaceAll", (JsValue)Symbols.ReplaceAll);
        DefineConstantProperty(constructor, "search", (JsValue)Symbols.Search);
        DefineConstantProperty(constructor, "split", (JsValue)Symbols.Split);
        DefineConstantProperty(constructor, "species", (JsValue)Symbols.Species);
        DefineConstantProperty(constructor, "isConcatSpreadable", (JsValue)Symbols.IsConcatSpreadable);
        DefineConstantProperty(constructor, "dispose", (JsValue)Symbols.Dispose);
        DefineConstantProperty(constructor, "asyncDispose", (JsValue)Symbols.AsyncDispose);
    }

    [JsConstructorSymbolGetter("dispose")]
    public static JsValue GetDispose()
    {
        return (JsValue)Symbols.Dispose;
    }

    [JsConstructorSymbolGetter("asyncDispose")]
    public static JsValue GetAsyncDispose()
    {
        return (JsValue)Symbols.AsyncDispose;
    }
}

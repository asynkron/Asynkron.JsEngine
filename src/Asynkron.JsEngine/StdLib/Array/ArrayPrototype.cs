using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Array", ToStringTag = "Array", ObjectKind = PrototypeObjectKind.Array)]
public sealed partial class ArrayPrototype
{


    protected override void ConfigurePrototype()
    {
        Realm.ArrayPrototype ??= Prototype;
        Realm.Logger?.LogInformation("ArrayPrototype configured: {Prototype}", Prototype);

        Prototype.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = 0d,
                Writable = true,
                Enumerable = false,
                Configurable = false
            });

        var iteratorKey = TypedAstSymbol.PropertyKey(Symbols.Iterator, Realm);
        if (Prototype.TryGetProperty("values", out var valuesFunction))
        {
            Prototype.DefineProperty(iteratorKey,
                new PropertyDescriptor
                {
                    Value = valuesFunction,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
        }

        DefineUnscopables();
    }

    private void DefineUnscopables()
    {
        var unscopables = new JsObject();
        unscopables.SetPrototype(null);

        void Flag(string name)
        {
            unscopables.DefineProperty(name,
                new PropertyDescriptor
                {
                    Value = true,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                });
        }

        Flag("copyWithin");
        Flag("entries");
        Flag("fill");
        Flag("find");
        Flag("findIndex");
        Flag("flat");
        Flag("flatMap");
        Flag("includes");
        Flag("keys");
        Flag("values");

        var symbol = Symbols.Unscopables;
        var key = TypedAstSymbol.PropertyKey(symbol);
        Prototype.DefineProperty(key,
            new PropertyDescriptor
            {
                Value = unscopables,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
    }
}

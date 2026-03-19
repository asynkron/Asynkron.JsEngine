#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime.Prototypes;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Array", ObjectKind = PrototypeObjectKind.Array)]
[JsSymbolAlias("iterator", "values")]
public sealed partial class ArrayPrototype
{
    protected override void ConfigurePrototype()
    {
        Realm.ArrayPrototype ??= Prototype;
        Realm.Logger?.LogInformation("ArrayPrototype configured: {Prototype}", Prototype);

        Prototype.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = true, Enumerable = false, Configurable = false });

        // [Symbol.iterator] is registered via code generation from [JsSymbolAlias] attribute
        DefineUnscopables();
    }

    private void DefineUnscopables()
    {
        var unscopables = new JsObject();
        unscopables.SetPrototype(null);

        Flag("copyWithin");
        Flag("entries");
        Flag("fill");
        Flag("find");
        Flag("findIndex");
        Flag("findLast");
        Flag("findLastIndex");
        Flag("flat");
        Flag("flatMap");
        Flag("includes");
        Flag("keys");
        Flag("toReversed");
        Flag("toSorted");
        Flag("toSpliced");
        Flag("values");
        // NOTE: "with" is NOT in @@unscopables per ES spec (it's a reserved keyword in strict mode)

        var key = SymbolKeys.Unscopables;
        Prototype.DefineProperty(key,
            new PropertyDescriptor { Value = unscopables, Writable = false, Enumerable = false, Configurable = true });
        return;

        void Flag(string name)
        {
            unscopables.DefineProperty(name,
                new PropertyDescriptor { Value = true, Writable = true, Enumerable = true, Configurable = true });
        }
    }
}

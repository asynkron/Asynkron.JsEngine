using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Array", ToStringTag = "Array")]
public sealed partial class ArrayPrototype
{


    protected override void ConfigurePrototype()
    {
        Realm.ArrayPrototype ??= Prototype;

        var iteratorKey = $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";
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
    }
}

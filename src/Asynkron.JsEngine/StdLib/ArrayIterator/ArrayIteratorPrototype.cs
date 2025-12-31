#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// ArrayIterator prototype for array iterators
/// </summary>
[JsPrototype("Array Iterator", ToStringTag = "Array Iterator")]
[JsSymbolAlias("iterator", "__selfIterator")]
public sealed partial class ArrayIteratorPrototype : JsPrototype
{
    [JsHostMethod("next", Length = 0d)]
    public JsValue Next(JsValue thisValue)
    {
        if (!thisValue.TryGetObject<JsArrayIterator>(out var iterator) || iterator is null)
        {
            throw ThrowTypeError("Array Iterator.prototype.next requires an Array Iterator instance", realm: Realm);
        }

        return iterator.Next();
    }

    [JsHostMethod("__selfIterator", Length = 0d)]
    public static JsValue SelfIterator(JsValue thisValue) => thisValue;

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.ArrayIteratorPrototype ??= Prototype as JsObject;
    }
}

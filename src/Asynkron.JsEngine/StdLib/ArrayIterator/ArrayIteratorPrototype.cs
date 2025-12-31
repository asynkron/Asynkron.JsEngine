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

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        var iteratorPrototype = Realm.IteratorPrototype ??= (JsObject)IteratorPrototype.CreatePrototype(Realm);
        if (!ReferenceEquals(Prototype.Prototype, iteratorPrototype))
        {
            Prototype.SetPrototype(iteratorPrototype);
        }

        Realm.ArrayIteratorPrototype ??= Prototype as JsObject;
    }
}

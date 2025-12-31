#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Map Iterator", ToStringTag = "Map Iterator")]
[JsSymbolAlias("iterator", "__selfIterator")]
public sealed partial class MapIteratorPrototype : JsPrototype
{
    [JsHostMethod("next", Length = 0d)]
    public JsValue Next(JsValue thisValue)
    {
        if (!thisValue.TryGetObject<JsMapIterator>(out var iterator) || iterator is null)
        {
            throw ThrowTypeError("Map Iterator.prototype.next requires a Map Iterator instance", realm: Realm);
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

        var iteratorPrototype = Realm.IteratorPrototype ??
                                (JsObject)IteratorPrototype.CreatePrototype(Realm);
        Realm.IteratorPrototype = iteratorPrototype;
        Prototype.SetPrototype(iteratorPrototype);

        Realm.MapIteratorPrototype ??= Prototype as JsObject;
    }
}

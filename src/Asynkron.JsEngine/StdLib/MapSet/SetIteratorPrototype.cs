#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Set Iterator", ToStringTag = "Set Iterator")]
[JsSymbolAlias("iterator", "__selfIterator")]
public sealed partial class SetIteratorPrototype : JsPrototype
{
    [JsHostMethod("next", Length = 0d)]
    public JsValue Next(JsValue thisValue)
    {
        if (!thisValue.TryGetObject<JsSetIterator>(out var iterator) || iterator is null)
        {
            throw ThrowTypeError("Set Iterator.prototype.next requires a Set Iterator instance", realm: Realm);
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

        Realm.SetIteratorPrototype ??= Prototype as JsObject;
    }
}

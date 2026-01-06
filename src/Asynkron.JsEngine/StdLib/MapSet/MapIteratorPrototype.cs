#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Map Iterator", ToStringTag = "Map Iterator")]
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

    protected override void ConfigurePrototype() =>
        ConfigureAsIteratorPrototype(p => Realm.MapIteratorPrototype ??= p);
}

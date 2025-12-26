#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Iterator", PrototypeType = typeof(IteratorPrototype), Length = 0d, DisplayName = "Iterator")]
public sealed partial class IteratorConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Iterator constructor
        // Creates a new Iterator instance
        var obj = PrepareThisObject(JsValue.Undefined, false);
        if (Prototype is not null && obj.Prototype is null)
        {
            obj.SetPrototype(Prototype);
        }
        obj.RealmState ??= Realm;
        return new JsValue(obj);
    }

    /* FLAKY */
    [JsConstructorMethod("from", Length = 1d)]
    public static JsValue From(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // TODO: Implement Iterator.from
        // Creates an iterator from an iterable
        throw new NotImplementedException("Iterator.from is not yet implemented");
    }
}

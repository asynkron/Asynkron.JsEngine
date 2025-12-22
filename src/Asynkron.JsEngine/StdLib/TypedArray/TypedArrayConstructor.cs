#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("TypedArray", PrototypeType = typeof(TypedArrayPrototype), Length = 0d, DisplayName = "TypedArray")]
public sealed partial class TypedArrayConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("TypedArray is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.TypedArrayPrototype ??= Prototype as JsObject;
        Realm.TypedArrayConstructor ??= constructor;

        constructor.DisallowConstruct = true;
        constructor.ConstructErrorMessage = "TypedArray is not a constructor";
        constructor.SetInvokeWithContext((_, _, _, newTarget) =>
        {
            if (!newTarget.IsUndefined)
            {
                throw ThrowTypeError("TypedArray is not a constructor", realm: Realm);
            }

            throw ThrowTypeError("TypedArray is not a constructor", realm: Realm);
        });
    }
}

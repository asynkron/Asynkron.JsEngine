#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ArrayBufferHelper;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ArrayBuffer", PrototypeType = typeof(ArrayBufferPrototype), Length = 1d, DisplayName = "ArrayBuffer")]
public sealed partial class ArrayBufferConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("ArrayBuffer constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = _constructor ?? ConstructFallback;
        return JsValue.FromObjectUnsafe(ConstructBuffer(args, target));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ArrayBufferPrototype ??= Prototype as JsObject;
        Realm.ArrayBufferConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("ArrayBuffer constructor requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : target;
            return JsValue.FromObjectUnsafe(ConstructBuffer(args, effectiveNewTarget));
        });
        // [Symbol.species] is registered via code generation from attribute
    }

    [JsConstructorMethod("isView", Length = 1d)]
    public static JsValue IsView(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNullOrUndefined)
        {
            return JsValue.False;
        }

        var arg = args[0];
        if (arg.TryGetObject<TypedArrayBase>(out _) || arg.TryGetObject<JsDataView>(out _))
        {
            return JsValue.True;
        }

        if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("_internalDataView", out var dv) &&
            dv.TryGetObject<JsDataView>(out _))
        {
            return JsValue.True;
        }

        return JsValue.False;
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    private object ConstructBuffer(IReadOnlyList<JsValue> args, IJsCallable newTarget) =>
        ArrayBufferHelper.ConstructBufferCore(args, newTarget, _constructor, Prototype, Realm,
            isShared: false, "ArrayBuffer", PrepareThisObject);
}

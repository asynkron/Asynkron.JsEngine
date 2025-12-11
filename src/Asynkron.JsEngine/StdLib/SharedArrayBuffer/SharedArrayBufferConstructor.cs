using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("SharedArrayBuffer", PrototypeType = typeof(SharedArrayBufferPrototype), Length = 1d,
    DisplayName = "SharedArrayBuffer")]
public sealed partial class SharedArrayBufferConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var target = _constructor ?? ConstructFallback;
        return ConstructBuffer(args, target);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.SharedArrayBufferPrototype ??= Prototype as JsObject;
        Realm.SharedArrayBufferConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget as IJsCallable ?? target;
            return ConstructBuffer(args, effectiveNewTarget);
        });

        var speciesKey = $"@@symbol:{TypedAstSymbol.For("Symbol.species").GetHashCode()}";
        constructor.DefineProperty(speciesKey,
            new PropertyDescriptor
            {
                Get = new HostFunction((thisVal, _) => thisVal),
                Enumerable = false,
                Configurable = true
            });
    }

    private object ConstructBuffer(IReadOnlyList<object?> args, IJsCallable newTarget)
    {
        var byteLength = args.GetArgument(0) switch
        {
            double d => (int)d,
            int i => i,
            _ => 0
        };

        var buffer = new JsArrayBuffer(byteLength, null, Realm);
        buffer.SetPrototype(Prototype);

        if (ReferenceEquals(newTarget, _constructor ?? newTarget))
        {
            return buffer;
        }

        var instance = PrepareThisObject(null, assignPrototype: false);
        var proto = ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        StoreInternalArrayBuffer(instance, buffer);
        return instance;
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("SharedArrayBuffer constructor not initialized");
}

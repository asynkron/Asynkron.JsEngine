using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ArrayBuffer", PrototypeType = typeof(ArrayBufferPrototype), Length = 1d, DisplayName = "ArrayBuffer")]
public sealed partial class ArrayBufferConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
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
        Realm.ArrayBufferPrototype ??= Prototype as JsObject;
        Realm.ArrayBufferConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget as IJsCallable ?? target;
            return ConstructBuffer(args, effectiveNewTarget);
        });

        var speciesKey = TypedAstSymbol.PropertyKey(Symbols.Species, Realm);
        constructor.DefineProperty(speciesKey,
            new PropertyDescriptor
            {
                Get = new HostFunction((thisVal, _) => thisVal),
                Enumerable = false,
                Configurable = true
            });

        constructor.SetHostedProperty("isView", ArrayBufferIsView, Realm);
    }

    private object ConstructBuffer(IReadOnlyList<object?> args, IJsCallable newTarget)
    {
        var byteLength = args.GetArgument(0) switch
        {
            double d => (int)d,
            int i => i,
            _ => 0
        };

        int? maxByteLength = null;
        if (args.Count > 1 && args[1] is JsObject opts)
        {
            if (opts.TryGetProperty("maxByteLength", out var maxVal))
            {
                var numeric = JsOps.ToNumber(maxVal);
                if (!double.IsNaN(numeric))
                {
                    maxByteLength = (int)numeric;
                }
            }
        }

        var buffer = new JsArrayBuffer(byteLength, maxByteLength, Realm);

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
        _constructor ?? throw new InvalidOperationException("ArrayBuffer constructor not initialized");
}

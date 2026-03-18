#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("FinalizationRegistry", PrototypeType = typeof(FinalizationRegistryPrototype), Length = 1d, DisplayName = "FinalizationRegistry")]
public sealed partial class FinalizationRegistryConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // This path is used when called with new (thisValue has IsConstructing set)
        return ConstructCore(args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        // Override the invoke handler to check newTarget (for calls without new)
        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            // ES2024 26.2.1.1 step 1: If NewTarget is undefined, throw a TypeError exception.
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("FinalizationRegistry constructor requires 'new'", realm: Realm);
            }

            return ConstructCore(args);
        });
    }

    private JsValue ConstructCore(IReadOnlyList<JsValue> args)
    {
        // ES2024 26.2.1.1 FinalizationRegistry(cleanupCallback)
        // 2. If IsCallable(cleanupCallback) is false, throw a TypeError exception.
        var cleanupCallback = args.GetArgument(0);
        if (!cleanupCallback.TryGetObject<IJsCallable>(out _))
        {
            throw ThrowTypeError("FinalizationRegistry: cleanup callback must be callable", realm: Realm);
        }

        // 3-7. Create the FinalizationRegistry instance with internal cells list
        var instance = CreateDefaultInstance();

        // Store the cleanup callback and cells list internally
        if (instance.TryGetObject<JsObject>(out var instanceObj))
        {
            instanceObj.SetProperty("__cleanupCallback__", cleanupCallback);
            instanceObj.SetProperty("__cells__", JsValue.FromJsArray(new JsArray(Realm)));
        }

        return instance;
    }
}

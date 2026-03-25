#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Internal state for a FinalizationRegistry instance.
/// Stored in a ConditionalWeakTable so it's invisible to JS code
/// (no own properties on the instance) per ES spec.
/// </summary>
internal sealed class FinalizationRegistryState
{
    public JsValue CleanupCallback { get; set; }
    public JsArray Cells { get; set; } = null!;
}

[JsConstructor("FinalizationRegistry", PrototypeType = typeof(FinalizationRegistryPrototype), Length = 1d, DisplayName = "FinalizationRegistry")]
public sealed partial class FinalizationRegistryConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    // Maps FinalizationRegistry JsObject instances to their internal state.
    // ConditionalWeakTable ensures state is GC'd when the instance is collected.
    internal static readonly ConditionalWeakTable<JsObject, FinalizationRegistryState> InternalState = new();

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructCore(args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("FinalizationRegistry constructor requires 'new'", realm: Realm);
            }

            return ConstructCore(args);
        });
    }

    private JsValue ConstructCore(IReadOnlyList<JsValue> args)
    {
        var cleanupCallback = args.GetArgument(0);
        if (!cleanupCallback.TryGetObject<IJsCallable>(out _))
        {
            throw ThrowTypeError("FinalizationRegistry: cleanup callback must be callable", realm: Realm);
        }

        var instance = CreateDefaultInstance();

        if (instance.TryGetObject<JsObject>(out var instanceObj))
        {
            InternalState.AddOrUpdate(instanceObj, new FinalizationRegistryState
            {
                CleanupCallback = cleanupCallback,
                Cells = new JsArray(Realm)
            });
        }

        return instance;
    }

    /// <summary>
    /// Gets the internal state for a FinalizationRegistry instance, or null if not found.
    /// </summary>
    internal static FinalizationRegistryState? GetState(JsObject obj) =>
        InternalState.TryGetValue(obj, out var state) ? state : null;
}

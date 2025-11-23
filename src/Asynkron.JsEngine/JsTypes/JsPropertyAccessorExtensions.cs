using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Helper extensions to simplify attaching host functions to property accessors.
/// </summary>
public static class JsPropertyAccessorExtensions
{
    public static void SetProperty(this IJsPropertyAccessor accessor, string name, HostFunction function,
        RealmState? realmState = null)
    {
        if (realmState is not null && function.RealmState is null)
        {
            function.RealmState = realmState;
        }

        accessor.SetProperty(name, (object?)function);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name, HostFunction function,
        RealmState? realmState = null)
    {
        accessor.SetProperty(name, function, realmState);
    }
}

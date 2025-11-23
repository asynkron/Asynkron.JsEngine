using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Helper extensions to simplify attaching host-backed functions to property accessors.
/// </summary>
public static class JsPropertyAccessorExtensions
{
    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<IReadOnlyList<object?>, object?> handler)
    {
        accessor.SetProperty(name, new HostFunction(handler));
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realmState)
    {
        accessor.SetProperty(name, new HostFunction(args => handler(args, realmState), realmState));
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        accessor.SetProperty(name, new HostFunction(handler));
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realmState)
    {
        accessor.SetProperty(name, new HostFunction(handler, realmState));
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name, HostFunction hostFunction,
        RealmState? realmState = null)
    {
        if (realmState is not null && hostFunction.RealmState is null)
        {
            hostFunction.RealmState = realmState;
        }

        accessor.SetProperty(name, hostFunction);
    }
}

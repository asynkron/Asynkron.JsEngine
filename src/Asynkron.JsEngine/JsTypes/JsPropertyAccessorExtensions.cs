using System.Collections.Generic;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Helper extensions to simplify attaching host functions to property accessors.
/// </summary>
public static class JsPropertyAccessorExtensions
{
    /// <summary>
    ///     Convenience overload for host-backed functions.
    /// </summary>
    public static void SetHostProperty(this IJsPropertyAccessor accessor, string name, Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        accessor.SetProperty(name, new HostFunction(handler));
    }

    /// <summary>
    ///     Convenience overload for realm-aware host-backed functions.
    /// </summary>
    public static void SetHostProperty(this IJsPropertyAccessor accessor, string name, Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler,
        RealmState? realmState)
    {
        accessor.SetProperty(name, new HostFunction(handler, realmState));
    }

    public static void SetProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        accessor.SetProperty(name, new HostFunction(handler));
    }

    public static void SetProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realmState)
    {
        accessor.SetProperty(name, new HostFunction(handler, realmState));
    }

    public static void SetProperty(this IJsPropertyAccessor accessor, string name, HostFunction function,
        RealmState? realmState = null)
    {
        if (realmState is not null && function.RealmState is null)
        {
            function.RealmState = realmState;
        }

        accessor.SetProperty(name, (object?)function);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        accessor.SetHostProperty(name, handler);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realmState)
    {
        accessor.SetHostProperty(name, handler, realmState);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name, HostFunction function,
        RealmState? realmState = null)
    {
        accessor.SetProperty(name, function, realmState);
    }
}

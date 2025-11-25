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
        var fn = new HostFunction(handler) { IsConstructor = false };
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realmState)
    {
        var fn = new HostFunction(args => handler(args, realmState), realmState) { IsConstructor = false };
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        var fn = new HostFunction(handler) { IsConstructor = false };
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realmState)
    {
        var fn = new HostFunction(handler, realmState) { IsConstructor = false };
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name, HostFunction hostFunction,
        RealmState? realmState = null)
    {
        if (realmState is not null && hostFunction.RealmState is null)
        {
            hostFunction.RealmState = realmState;
        }

        hostFunction.IsConstructor = false;
        accessor.SetProperty(name, hostFunction);
    }
}

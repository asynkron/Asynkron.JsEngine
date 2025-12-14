using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Helper extensions to simplify attaching host-backed functions to property accessors.
/// </summary>
public static class JsPropertyAccessorExtensions
{
    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<IReadOnlyList<JsValue>, JsValue> handler)
    {
        var fn = new HostFunction(handler, isConstructor: false);
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<IReadOnlyList<JsValue>, RealmState?, JsValue> handler, RealmState? realmState)
    {
        var fn = new HostFunction(args => handler(args, realmState), realmState, isConstructor: false);
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<JsValue, IReadOnlyList<JsValue>, JsValue> handler)
    {
        var fn = new HostFunction(handler, isConstructor: false);
        accessor.SetProperty(name, fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<JsValue, IReadOnlyList<JsValue>, RealmState?, JsValue> handler, RealmState? realmState)
    {
        var fn = new HostFunction((thisVal, args) => handler(thisVal, args, realmState), realmState, isConstructor: false);
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

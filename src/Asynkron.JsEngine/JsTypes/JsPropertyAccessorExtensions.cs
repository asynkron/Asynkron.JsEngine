#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Helper extensions to simplify attaching host-backed functions to property accessors.
/// </summary>
public static class JsPropertyAccessorExtensions
{
    // New JsValue-based signatures (preferred)
    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        JsSimpleHandler handler)
    {
        var fn = new HostFunction(handler, isConstructor: false);
        accessor.SetProperty(name, (JsValue)fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<IReadOnlyList<JsValue>, RealmState?, JsValue> handler, RealmState? realmState)
    {
        JsSimpleHandler wrappedHandler = args => handler(args, realmState);
        var fn = new HostFunction(wrappedHandler, realmState, false);
        accessor.SetProperty(name, (JsValue)fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        JsHostHandler handler)
    {
        var fn = new HostFunction(handler, isConstructor: false);
        accessor.SetProperty(name, (JsValue)fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name,
        Func<JsValue, IReadOnlyList<JsValue>, RealmState?, JsValue> handler, RealmState? realmState)
    {
        JsHostHandler wrappedHandler = (thisVal, args) => handler(thisVal, args, realmState);
        var fn = new HostFunction(wrappedHandler, realmState, false);
        accessor.SetProperty(name, (JsValue)fn);
    }

    public static void SetHostedProperty(this IJsPropertyAccessor accessor, string name, HostFunction hostFunction,
        RealmState? realmState = null)
    {
        if (realmState is not null && hostFunction.RealmState is null)
        {
            hostFunction.RealmState = realmState;
        }

        hostFunction.IsConstructor = false;
        accessor.SetProperty(name, (JsValue)hostFunction);
    }
}

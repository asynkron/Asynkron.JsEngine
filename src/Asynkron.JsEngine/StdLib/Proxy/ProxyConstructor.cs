#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Proxy", PrototypeType = typeof(ProxyPrototype), Length = 2d, DisplayName = "Proxy")]
public sealed partial class ProxyConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = RequireProxyObject(args.GetArgument(0), "Proxy target must be an object", Realm);
        var handler = RequireProxyObject(args.GetArgument(1), "Proxy handler must be an object", Realm);
        return JsValue.FromJsProxy(new JsProxy(target, handler, Realm));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        constructor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = Symbol.Undefined,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        // Marker to make class heritage resolution reject Proxy as a base.
        constructor.DefineProperty("__proxyHasNoPrototype__",
            new PropertyDescriptor
            {
                Value = true,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
    }

    [JsConstructorMethod("revocable", Length = 2d)]
    public static JsValue Revocable(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var target = RequireProxyObject(args.GetArgument(0), "Proxy target must be an object", realm);
        var handler = RequireProxyObject(args.GetArgument(1), "Proxy handler must be an object", realm);

        var proxy = new JsProxy(target, handler, realm!);
        var container = new JsObject();
        container.SetProperty("proxy", JsValue.FromJsProxy(proxy));

        container.SetHostedProperty("revoke", (_, _) =>
        {
            proxy.Handler = null;
            return JsValue.Undefined;
        });

        return new JsValue(container);
    }

    private static IJsObjectLike RequireProxyObject(JsValue candidate, string message, RealmState? realm)
    {
        if (candidate.TryGetObject<IJsObjectLike>(out var obj) && obj is not null)
        {
            return obj;
        }

        throw ThrowTypeError(message, realm: realm);
    }
}

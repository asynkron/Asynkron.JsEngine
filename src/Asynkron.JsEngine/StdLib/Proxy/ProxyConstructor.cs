using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Proxy", PrototypeType = typeof(ProxyPrototype), Length = 2d, DisplayName = "Proxy")]
public sealed partial class ProxyConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = RequireObject(args.GetArgument(0), "Proxy target must be an object");
        var handler = RequireObject(args.GetArgument(1), "Proxy handler must be an object");
        return JsValue.FromObjectUnsafe(new JsProxy(target, handler, Realm));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        constructor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = Symbol.Undefined,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true,
            });

        DefineBuiltinFunction(constructor.PropertiesObject, "revocable",
            new HostFunction(args => Revocable(args), Realm, isConstructor: false), 2);

        // Marker to make class heritage resolution reject Proxy as a base.
        constructor.DefineProperty("__proxyHasNoPrototype__", new PropertyDescriptor
        {
            Value = true,
            Writable = false,
            Enumerable = false,
            Configurable = false,
            HasValue = true,
            HasWritable = true,
            HasEnumerable = true,
            HasConfigurable = true,
        });
    }

    private IJsObjectLike RequireObject(JsValue candidate, string message)
    {
        if (candidate.TryGetObject<IJsObjectLike>(out var obj) && obj is not null)
        {
            return obj;
        }

        throw ThrowTypeError(message, realm: Realm);
    }

    private JsValue Revocable(IReadOnlyList<JsValue> args)
    {
        var target = RequireObject(args.GetArgument(0), "Proxy target must be an object");
        var handler = RequireObject(args.GetArgument(1), "Proxy handler must be an object");

        var proxy = new JsProxy(target, handler, Realm);
        var container = new JsObject();
        container.SetProperty("proxy", JsValue.FromObjectUnsafe(proxy));

        container.SetHostedProperty("revoke", (__, _) =>
        {
            proxy.Handler = null;
            return JsValue.Undefined;
        });

        return new JsValue(container);
    }
}

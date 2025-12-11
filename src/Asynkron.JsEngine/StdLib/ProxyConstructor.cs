using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Proxy", PrototypeType = typeof(ProxyPrototype), Length = 2d, DisplayName = "Proxy")]
public sealed partial class ProxyConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var target = RequireObject(args.GetArgument(0), "Proxy target must be an object");
        var handler = RequireObject(args.GetArgument(1), "Proxy handler must be an object");
        return new JsProxy(target, handler, Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        constructor.DefineProperty("prototype",
            new PropertyDescriptor { Value = Symbol.Undefined, Writable = false, Enumerable = false, Configurable = false });

        DefineBuiltinFunction(constructor.PropertiesObject, "revocable",
            new HostFunction(Revocable, Realm, isConstructor: false), 2);
    }

    private IJsObjectLike RequireObject(object? candidate, string message)
    {
        if (candidate is not IJsObjectLike obj)
        {
            throw ThrowTypeError(message, realm: Realm);
        }

        return obj;
    }

    private object? Revocable(object? _, IReadOnlyList<object?> args)
    {
        var target = RequireObject(args.GetArgument(0), "Proxy target must be an object");
        var handler = RequireObject(args.GetArgument(1), "Proxy handler must be an object");

        var proxy = new JsProxy(target, handler, Realm);
        var container = new JsObject();
        container.SetProperty("proxy", proxy);

        container.SetHostedProperty("revoke", (__, _) =>
        {
            proxy.Handler = null;
            return Symbol.Undefined;
        });

        return container;
    }
}

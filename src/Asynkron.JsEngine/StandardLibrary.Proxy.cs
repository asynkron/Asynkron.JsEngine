using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static HostFunction CreateProxyConstructor(Runtime.RealmState realm)
    {
        JsObject? proxyPrototype = null;

        var proxyConstructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                throw new NotSupportedException("Proxy requires a target and handler.");
            }

            var target = args[0];
            var handler = args[1];

            if (target is not IJsObjectLike)
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Proxy target must be an object"], null)
                    : new InvalidOperationException("Proxy target must be an object.");
                throw new ThrowSignal(error);
            }

            if (handler is not IJsObjectLike handlerObj)
            {
                var error = TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Proxy handler must be an object"], null)
                    : new InvalidOperationException("Proxy handler must be an object.");
                throw new ThrowSignal(error);
            }

            var proxy = new JsProxy(target!, handlerObj);
            if (proxyPrototype is not null)
            {
                proxy.SetPrototype(proxyPrototype);
            }

            return proxy;
        });

        proxyConstructor.RealmState = realm;
        if (realm.FunctionPrototype is not null)
        {
            proxyConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        proxyPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            proxyPrototype.SetPrototype(realm.ObjectPrototype);
        }
        proxyConstructor.SetProperty("prototype", proxyPrototype);

        proxyConstructor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "Proxy",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        proxyConstructor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 2d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        var revocableFn = new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Proxy.revocable requires a target and handler"], null)
                    : new InvalidOperationException("Proxy.revocable requires a target and handler.");
                throw new ThrowSignal(error);
            }

            var target = args[0];
            var handler = args[1];

            if (target is not IJsObjectLike)
            {
                var error = TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Proxy target must be an object"], null)
                    : new InvalidOperationException("Proxy target must be an object.");
                throw new ThrowSignal(error);
            }

            if (handler is not IJsObjectLike handlerObj)
            {
                var error = TypeErrorConstructor is IJsCallable ctor3
                    ? ctor3.Invoke(["Proxy handler must be an object"], null)
                    : new InvalidOperationException("Proxy handler must be an object.");
                throw new ThrowSignal(error);
            }

            var proxy = new JsProxy(target!, handlerObj);
            if (proxyPrototype is not null)
            {
                proxy.SetPrototype(proxyPrototype);
            }

            var container = new JsObject();
            container.SetProperty("proxy", proxy);
            container.SetProperty("revoke", new HostFunction((_, _) =>
            {
                proxy.Handler = null;
                return Symbols.Undefined;
            }));

            return container;
        });
        revocableFn.IsConstructor = false;
        proxyConstructor.DefineProperty("revocable", new PropertyDescriptor
        {
            Value = revocableFn,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        return proxyConstructor;
    }

}

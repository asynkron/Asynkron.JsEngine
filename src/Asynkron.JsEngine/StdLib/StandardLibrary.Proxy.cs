using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateProxyConstructor(RealmState realm)
    {
        var proxyConstructor = new HostFunction((_, args) =>
        {
            if (args.Count < 2)
            {
                throw new NotSupportedException("Proxy requires a target and handler.");
            }

            var target = args[0];
            var handler = args[1];

            if (target is not IJsObjectLike targetObject)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Proxy target must be an object"], null)
                    : new InvalidOperationException("Proxy target must be an object.");
                throw new ThrowSignal(error);
            }

            if (handler is not IJsObjectLike handlerObj)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Proxy handler must be an object"], null)
                    : new InvalidOperationException("Proxy handler must be an object.");
                throw new ThrowSignal(error);
            }

            var proxy = new JsProxy(targetObject, handlerObj, realm);
            return proxy;
        });

        proxyConstructor.RealmState = realm;
        if (realm.FunctionPrototype is not null)
        {
            proxyConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        // Per spec 26.2.1: Proxy constructor does NOT have a 'prototype' property
        // This makes it non-subclassable: class P extends Proxy {} will throw TypeError
        // because Proxy.prototype is undefined (not an Object or null)
        proxyConstructor.DeleteProperty("prototype");
        proxyConstructor.SetProperty("prototype", Symbol.Undefined);

        proxyConstructor.DefineProperty("name",
            new PropertyDescriptor { Value = "Proxy", Writable = false, Enumerable = false, Configurable = true });

        proxyConstructor.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });

        var revocableFn = new HostFunction((_, args) =>
        {
            if (args.Count < 2)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Proxy.revocable requires a target and handler"], null)
                    : new InvalidOperationException("Proxy.revocable requires a target and handler.");
                throw new ThrowSignal(error);
            }

            var target = args[0];
            var handler = args[1];

            if (target is not IJsObjectLike targetObject)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Proxy target must be an object"], null)
                    : new InvalidOperationException("Proxy target must be an object.");
                throw new ThrowSignal(error);
            }

            if (handler is not IJsObjectLike handlerObj)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor3
                    ? ctor3.Invoke(["Proxy handler must be an object"], null)
                    : new InvalidOperationException("Proxy handler must be an object.");
                throw new ThrowSignal(error);
            }

            var proxy = new JsProxy(targetObject, handlerObj, realm);
            var container = new JsObject();
            container.SetProperty("proxy", proxy);

            container.SetHostedProperty("revoke", Revoke);

            return container;

            object? Revoke(object? _, IReadOnlyList<object?> __)
            {
                proxy.Handler = null;
                return Symbol.Undefined;
            }
        }, isConstructor: false);
        proxyConstructor.DefineProperty("revocable",
            new PropertyDescriptor { Value = revocableFn, Writable = true, Enumerable = false, Configurable = true });

        return proxyConstructor;
    }
}

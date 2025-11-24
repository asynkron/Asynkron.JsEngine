using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateFunctionConstructor(RealmState realm)
    {
        // Minimal Function constructor: for now we ignore the body and
        // arguments and just return a no-op function value.
        HostFunction functionConstructor = null!;

        functionConstructor = new HostFunction((thisValue, _) =>
        {
            var realm = functionConstructor.Realm ?? thisValue as JsObject;
            return new HostFunction((_, _) => Symbols.Undefined)
            {
                Realm = realm, RealmState = functionConstructor.RealmState
            };
        });
        functionConstructor.RealmState = realm;

        // Function.call: when used as `fn.call(thisArg, ...args)` the
        // target function is `fn` (the `this` value). We implement this
        // directly so that binding `Function.call` or
        // `Function.prototype.call` produces helpers that behave like
        // `Function.prototype.call`.
        var callHelper = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsCallable target)
            {
                return Symbols.Undefined;
            }

            object? thisArg = Symbols.Undefined;
            var callArgs = Array.Empty<object?>();

            if (args.Count > 0)
            {
                thisArg = args[0];
                if (args.Count > 1)
                {
                    callArgs = args.Skip(1).ToArray();
                }
            }

            return target.Invoke(callArgs, thisArg);
        });
        callHelper.Realm = functionConstructor.Realm;
        callHelper.RealmState = functionConstructor.RealmState;

        functionConstructor.SetProperty("call", callHelper);

        // Provide a minimal `Function.prototype` object that exposes the
        // same call helper so patterns like
        // `Function.prototype.call.bind(Object.prototype.hasOwnProperty)`
        // work as expected.
        var functionPrototype = new JsObject();
        functionPrototype.SetProperty("call", callHelper);
        if (realm.ObjectPrototype is not null)
        {
            functionPrototype.SetPrototype(realm.ObjectPrototype);
        }
        functionPrototype.SetProperty("constructor", functionConstructor);

        var hasInstanceKey = $"@@symbol:{TypedAstSymbol.For("Symbol.hasInstance").GetHashCode()}";
        var hasInstance = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsPropertyAccessor)
            {
                throw ThrowTypeError("Function.prototype[@@hasInstance] called on non-object", null, realm);
            }

            var candidate = args.Count > 0 ? args[0] : Symbols.Undefined;
            if (candidate is not JsObject && candidate is not IJsObjectLike)
            {
                return false;
            }

            if (!JsOps.TryGetPropertyValue(thisValue, "prototype", out var protoVal, null) ||
                protoVal is not JsObject prototypeObject)
            {
                throw ThrowTypeError("Function has non-object prototype in instanceof check", null, realm);
            }

            var cursor = candidate switch
            {
                JsObject obj => obj.Prototype,
                IJsObjectLike objectLike => objectLike.Prototype,
                _ => null
            };

            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, prototypeObject))
                {
                    return true;
                }

                cursor = cursor.Prototype;
            }

            return false;
        })
        {
            RealmState = realm
        };
        functionPrototype.SetProperty(hasInstanceKey, hasInstance);
        realm.FunctionPrototype ??= functionPrototype;
        functionConstructor.SetProperty("prototype", functionPrototype);
        functionConstructor.Properties.SetPrototype(functionPrototype);

        return functionConstructor;
    }
}

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static IJsCallable CreateFunctionConstructor(Runtime.RealmState realm)
    {
        // Minimal Function constructor: for now we ignore the body and
        // arguments and just return a no-op function value.
        HostFunction functionConstructor = null!;

        functionConstructor = new HostFunction((thisValue, args) =>
        {
            var realm = functionConstructor.Realm ?? thisValue as JsObject;
            return new HostFunction((innerThis, innerArgs) => Symbols.Undefined)
            {
                Realm = realm,
                RealmState = functionConstructor.RealmState
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
        var hasInstanceKey = $"@@symbol:{TypedAstSymbol.For("Symbol.hasInstance").GetHashCode()}";
        functionPrototype.SetProperty(hasInstanceKey, new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsCallable)
            {
                throw new InvalidOperationException("Function.prototype[@@hasInstance] called on non-callable value.");
            }

            var candidate = args.Count > 0 ? args[0] : Symbols.Undefined;
            if (candidate is not JsObject obj)
            {
                return false;
            }

            JsObject? targetPrototype = null;
            if (thisValue is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoVal) &&
                protoVal is JsObject protoObj)
            {
                targetPrototype = protoObj;
            }

            if (targetPrototype is null)
            {
                return false;
            }

            var cursor = obj;
            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, targetPrototype))
                {
                    return true;
                }

                cursor = cursor.Prototype;
            }

            return false;
        }));
        realm.FunctionPrototype ??= functionPrototype;
        functionConstructor.SetProperty("prototype", functionPrototype);
        functionConstructor.Properties.SetPrototype(functionPrototype);

        return functionConstructor;
    }

}

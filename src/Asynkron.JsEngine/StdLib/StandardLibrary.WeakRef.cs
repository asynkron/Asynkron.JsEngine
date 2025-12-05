using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateWeakRefConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);

        var constructor = new HostFunction((thisValue, args) =>
        {
            var target = args.Count > 0 ? args[0] : null;
            var instance = thisValue as JsObject ?? new JsObject();

            if (instance.Prototype is null)
            {
                instance.SetPrototype(prototype);
            }

            instance.SetProperty("_target", target);
            return instance;
        }, realm, isConstructor: true);

        var deref = new HostFunction((thisValue, _) =>
        {
            if (thisValue is JsObject obj && obj.TryGetProperty("_target", out var stored))
            {
                return stored;
            }

            return Symbol.Undefined;
        }, realm, isConstructor: false);

        prototype.SetHostedProperty("deref", deref);
        prototype.SetProperty("constructor", constructor);
        constructor.SetProperty("prototype", prototype);

        return constructor;
    }
}

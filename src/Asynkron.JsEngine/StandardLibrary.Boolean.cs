using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    /// <summary>
    /// Creates the Boolean constructor function.
    /// </summary>
    public static HostFunction CreateBooleanConstructor(Runtime.RealmState realm)
    {
        // Boolean(value) -> boolean primitive using ToBoolean semantics.
        var booleanConstructor = new HostFunction((thisValue, args) =>
        {
            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            var coerced = JsOps.ToBoolean(value);

            // When called with `new`, thisValue is the newly-created object;
            // store the primitive value so property lookups (e.g. ToPropertyName)
            // can recover it.
            if (thisValue is JsObject obj)
            {
                obj.SetProperty("__value__", coerced);
                return obj;
            }

            return coerced;
        });

        // Expose Boolean.prototype so user code can attach methods (e.g.
        // Boolean.prototype.toJSONString in string-tagcloud.js).
        var prototype = new JsObject();
        realm.BooleanPrototype ??= prototype;
        BooleanPrototype ??= prototype;
        if (realm.ObjectPrototype is not null && prototype.Prototype is null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }
        booleanConstructor.SetProperty("prototype", prototype);

        return booleanConstructor;
    }

    /// <summary>
    /// Creates a wrapper object for a boolean primitive so that auto-boxed
    /// booleans can see methods added to Boolean.prototype.
    /// </summary>
    public static JsObject CreateBooleanWrapper(bool value, EvaluationContext? context = null)
    {
        var booleanObj = new JsObject
        {
            ["__value__"] = value
        };

        var prototype = context?.RealmState?.BooleanPrototype ?? BooleanPrototype;
        if (prototype is not null)
        {
            booleanObj.SetPrototype(prototype);
        }

        return booleanObj;
    }
}

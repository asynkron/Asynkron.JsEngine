using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates the Boolean constructor function.
    /// </summary>
    public static HostFunction CreateBooleanConstructor(RealmState realm)
    {
        // Boolean(value) -> boolean primitive using ToBoolean semantics.
        var booleanConstructor = new HostFunction((thisValue, args) =>
        {
            var value = args.Count > 0 ? args[0] : Symbol.Undefined;
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
        if (realm.ObjectPrototype is not null && prototype.Prototype is null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }

        DefineBuiltinFunction(prototype, "toString", new HostFunction(BooleanPrototypeToString), 0,
            isConstructor: false);
        DefineBuiltinFunction(prototype, "valueOf", new HostFunction(BooleanPrototypeValueOf), 0,
            isConstructor: false);

        booleanConstructor.SetProperty("prototype", prototype);

        return booleanConstructor;

        object? BooleanPrototypeToString(object? thisValue, IReadOnlyList<object?> _)
        {
            return RequireBooleanReceiver(thisValue) ? "true" : "false";
        }

        object? BooleanPrototypeValueOf(object? thisValue, IReadOnlyList<object?> _)
        {
            return RequireBooleanReceiver(thisValue);
        }

        bool RequireBooleanReceiver(object? receiver)
        {
            return receiver switch
            {
                bool b => b,
                JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner is bool b => b,
                IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var inner)
                    && inner is bool b => b,
                _ => throw ThrowTypeError("Boolean.prototype valueOf called on non-boolean object", realm: realm)
            };
        }
    }

    /// <summary>
    ///     Creates a wrapper object for a boolean primitive so that auto-boxed
    ///     booleans can see methods added to Boolean.prototype.
    /// </summary>
    public static JsObject CreateBooleanWrapper(bool value, EvaluationContext? context = null, RealmState? realm = null)
    {
        var booleanObj = new JsObject { ["__value__"] = value };

        var prototype = context?.RealmState?.BooleanPrototype ?? realm?.BooleanPrototype;
        if (prototype is not null)
        {
            booleanObj.SetPrototype(prototype);
        }

        return booleanObj;
    }
}

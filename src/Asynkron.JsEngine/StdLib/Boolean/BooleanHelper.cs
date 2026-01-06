#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class BooleanHelper
{
    /// <summary>
    ///     Creates the Boolean constructor function.
    /// </summary>
    public static HostFunction CreateBooleanConstructor(RealmState realm)
    {
        return BooleanConstructor.CreateConstructor(realm);
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

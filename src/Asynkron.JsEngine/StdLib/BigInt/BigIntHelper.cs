#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class BigIntHelper
{
    public static JsObject CreateBigIntWrapper(JsBigInt value, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var wrapper = new JsObject { ["__value__"] = value };

        var prototype = context?.RealmState?.BigIntPrototype ?? realm?.BigIntPrototype;
        if (prototype is not null)
        {
            wrapper.SetPrototype(prototype);
        }

        return wrapper;
    }
}

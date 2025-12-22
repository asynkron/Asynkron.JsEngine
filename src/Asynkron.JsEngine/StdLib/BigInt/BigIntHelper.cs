using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static class BigIntHelper
{
    public static IJsCallable CreateBigIntFunction(RealmState realm)
    {
        return BigIntConstructor.CreateConstructor(realm);
    }

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

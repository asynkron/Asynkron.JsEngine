using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static class WeakRefHelper
{
    public static IJsCallable CreateWeakRefConstructor(RealmState realm)
    {
        return WeakRefConstructor.CreateConstructor(realm);
    }
}

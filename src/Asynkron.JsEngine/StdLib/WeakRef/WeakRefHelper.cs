#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class WeakRefHelper
{
    public static IJsCallable CreateWeakRefConstructor(RealmState realm)
    {
        return WeakRefConstructor.CreateConstructor(realm);
    }
}

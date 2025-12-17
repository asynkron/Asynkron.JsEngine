using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateWeakRefConstructor(RealmState realm)
    {
        return WeakRefConstructor.CreateConstructor(realm);
    }
}

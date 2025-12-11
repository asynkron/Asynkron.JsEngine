using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateMapConstructor(RealmState realm)
    {
        return MapConstructor.CreateConstructor(realm);
    }

    public static IJsCallable CreateSetConstructor(RealmState realm)
    {
        return SetConstructor.CreateConstructor(realm);
    }

    public static IJsCallable CreateWeakMapConstructor(RealmState realm)
    {
        return WeakMapConstructor.CreateConstructor(realm);
    }

    public static IJsCallable CreateWeakSetConstructor(RealmState realm)
    {
        return WeakSetConstructor.CreateConstructor(realm);
    }
}

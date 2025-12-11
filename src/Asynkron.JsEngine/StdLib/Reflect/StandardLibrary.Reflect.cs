using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateReflectObject(RealmState realm)
    {
        return (JsObject)ReflectPrototype.CreatePrototype(realm);
    }
}

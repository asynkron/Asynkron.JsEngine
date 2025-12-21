using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateFunctionConstructor(RealmState realm, JsEngine engine)
    {
        // engine parameter kept for API compatibility but is now accessed via realm.Engine
        _ = engine;
        return FunctionConstructor.CreateConstructor(realm);
    }
}

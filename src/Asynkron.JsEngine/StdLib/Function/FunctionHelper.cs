#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class FunctionHelper
{
    public static IJsCallable CreateFunctionConstructor(RealmState realm, JsEngine engine)
    {
        // engine parameter kept for API compatibility but is now accessed via realm.Engine
        _ = engine;
        return FunctionConstructor.CreateConstructor(realm);
    }
}

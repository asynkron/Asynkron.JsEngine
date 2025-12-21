using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static class SharedArrayBufferHelper
{
    public static HostFunction CreateSharedArrayBufferConstructor(RealmState realm)
    {
        return SharedArrayBufferConstructor.CreateConstructor(realm);
    }
}

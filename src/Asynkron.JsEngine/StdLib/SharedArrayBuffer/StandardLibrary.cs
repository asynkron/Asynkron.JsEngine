#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class SharedArrayBufferHelper
{
    public static HostFunction CreateSharedArrayBufferConstructor(RealmState realm)
    {
        return SharedArrayBufferConstructor.CreateConstructor(realm);
    }
}

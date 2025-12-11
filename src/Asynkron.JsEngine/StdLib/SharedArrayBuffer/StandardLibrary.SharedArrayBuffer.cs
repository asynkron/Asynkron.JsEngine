using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateSharedArrayBufferConstructor(RealmState realm)
    {
        return SharedArrayBufferConstructor.CreateConstructor(realm);
    }
}

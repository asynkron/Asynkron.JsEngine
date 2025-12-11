using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateProxyConstructor(RealmState realm)
    {
        return ProxyConstructor.CreateConstructor(realm);
    }
}

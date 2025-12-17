using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateProxyConstructor(RealmState realm)
    {
        var ctor = ProxyConstructor.CreateConstructor(realm);

        // Ensure Proxy.prototype is undefined (ECMA-262 26.2.1)
        ctor.SetProperty("prototype", Symbol.Undefined);
        ctor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = Symbol.Undefined,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true,
            });

        return ctor;
    }
}

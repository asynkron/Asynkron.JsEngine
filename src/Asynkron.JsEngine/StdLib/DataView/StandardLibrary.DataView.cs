#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateDataViewConstructor(RealmState realm)
    {
        return DataViewConstructor.CreateConstructor(realm);
    }
}

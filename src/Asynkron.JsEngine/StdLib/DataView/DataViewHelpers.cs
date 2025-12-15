using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static void StoreInternalDataView(JsObject obj, JsDataView dataView)
    {
        obj.SetProperty("_internalDataView", dataView);
    }

    internal static JsDataView RequireDataView(object? thisVal, RealmState realm)
    {
        // Handle boxed JsValue struct first - unwrap to get the underlying object
        if (thisVal is JsValue jsVal)
        {
            thisVal = jsVal.ToObject();
        }

        if (thisVal is JsDataView directDv)
        {
            return directDv;
        }

        if (thisVal is JsObject obj)
        {
            var descriptor = obj.GetOwnPropertyDescriptor("_internalDataView");
            if (descriptor?.Value is JsDataView internalDv)
            {
                return internalDv;
            }
            // Handle case where value is boxed JsValue struct
            if (descriptor?.Value is JsValue dvJsVal && dvJsVal.TryGetObject<JsDataView>(out var dvFromJsValue))
            {
                return dvFromJsValue;
            }
        }

        throw ThrowTypeError("DataView method called on incompatible receiver", realm: realm);
    }
}

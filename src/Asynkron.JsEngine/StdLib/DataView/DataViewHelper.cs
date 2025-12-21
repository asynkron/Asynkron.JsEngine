using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public static class DataViewHelper
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
            if (jsVal.Kind == JsValueKind.Object && jsVal.ObjectValue is { } objVal)
            {
                thisVal = objVal;
            }
            else if (jsVal.IsNullOrUndefined)
            {
                throw ThrowTypeError("DataView method called on incompatible receiver", realm: realm);
            }
            else
            {
                thisVal = jsVal.ObjectValue;
            }
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

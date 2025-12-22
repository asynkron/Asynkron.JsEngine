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
            if (jsVal is { Kind: JsValueKind.Object, ObjectValue: { } objVal })
            {
                thisVal = objVal;
            }
            else
            {
                if (jsVal.IsNullOrUndefined)
                {
                    throw ThrowTypeError("DataView method called on incompatible receiver", realm: realm);
                }

                thisVal = jsVal.ObjectValue;
            }
        }

        switch (thisVal)
        {
            case JsDataView directDv:
                return directDv;
            case JsObject obj:
            {
                var descriptor = obj.GetOwnPropertyDescriptor("_internalDataView");
                switch (descriptor?.Value)
                {
                    case JsDataView internalDv:
                        return internalDv;
                    // Handle case where the value is boxed JsValue struct
                    case JsValue dvJsVal when dvJsVal.TryGetObject<JsDataView>(out var dvFromJsValue):
                        return dvFromJsValue;
                }

                break;
            }
        }

        throw ThrowTypeError("DataView method called on incompatible receiver", realm: realm);
    }
}

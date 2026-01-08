
namespace Asynkron.JsEngine.StdLib;

public static class DataViewHelper
{
    internal static void StoreInternalDataView(JsObject obj, JsDataView dataView)
    {
        obj.SetProperty("_internalDataView", dataView);
    }
}

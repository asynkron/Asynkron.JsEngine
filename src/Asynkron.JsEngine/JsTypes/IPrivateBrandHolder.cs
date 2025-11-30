namespace Asynkron.JsEngine.JsTypes;

public interface IPrivateBrandHolder
{
    void AddPrivateBrand(object brand);
    bool HasPrivateBrand(object brand);
}

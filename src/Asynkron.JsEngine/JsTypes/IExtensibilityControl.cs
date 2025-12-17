namespace Asynkron.JsEngine.JsTypes;

public interface IExtensibilityControl
{
    bool IsExtensible { get; }
    void PreventExtensions();
}

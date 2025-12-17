namespace Asynkron.JsEngine.JsTypes;

public interface IPropertyDefinitionHost
{
    bool TryDefineProperty(string name, PropertyDescriptor descriptor);
}

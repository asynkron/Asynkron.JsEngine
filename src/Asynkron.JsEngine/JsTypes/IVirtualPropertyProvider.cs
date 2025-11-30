namespace Asynkron.JsEngine.JsTypes;

public interface IVirtualPropertyProvider
{
    bool TryGetOwnProperty(string name, out object? value, out PropertyDescriptor? descriptor);
    IEnumerable<string> GetEnumerableKeys();
}

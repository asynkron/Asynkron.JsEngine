#region

#endregion

namespace Asynkron.JsEngine.JsTypes;

public abstract class JsDisposableStackBase : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider, IAsJsValue
{
    private readonly JsObject _properties = new();
    private readonly JsValue _cachedJsValue;
    private readonly List<DisposableStackRecord> _records = [];

    protected JsDisposableStackBase()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    internal bool IsDisposed { get; private set; }

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        return _properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    public IEnumerable<string> Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public bool Delete(string name)
    {
        return _properties.DeleteOwnProperty(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _properties.TryDefineProperty(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    internal void AddRecord(IJsCallable disposeMethod, JsValue thisArg, JsValue[] args)
    {
        _records.Add(new DisposableStackRecord(disposeMethod, thisArg, args));
    }

    internal bool TryPopRecord(out DisposableStackRecord record)
    {
        if (_records.Count == 0)
        {
            record = default;
            return false;
        }

        var index = _records.Count - 1;
        record = _records[index];
        _records.RemoveAt(index);
        return true;
    }

    internal void TransferRecordsTo(JsDisposableStackBase target)
    {
        if (_records.Count == 0)
        {
            return;
        }

        target._records.AddRange(_records);
        _records.Clear();
    }

    internal void ClearRecords() => _records.Clear();

    internal void MarkDisposed() => IsDisposed = true;
}

internal readonly struct DisposableStackRecord
{
    internal DisposableStackRecord(IJsCallable disposeMethod, JsValue thisArg, JsValue[] args)
    {
        DisposeMethod = disposeMethod;
        ThisArg = thisArg;
        Args = args;
    }

    internal IJsCallable DisposeMethod { get; }
    internal JsValue ThisArg { get; }
    internal JsValue[] Args { get; }

    internal JsValue Invoke()
    {
        return DisposeMethod.Invoke(Args, ThisArg);
    }
}

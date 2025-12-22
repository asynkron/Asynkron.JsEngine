namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a live export binding whose value is resolved on demand.
/// </summary>
internal sealed class LiveExportBinding(Func<JsValue> getter)
{
    private readonly Func<JsValue> _getter = getter ?? throw new ArgumentNullException(nameof(getter));

    public JsValue GetValue()
    {
        return _getter();
    }
}

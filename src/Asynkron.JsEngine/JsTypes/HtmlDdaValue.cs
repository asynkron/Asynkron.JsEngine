namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Minimal stub for HTMLDDA-like values (e.g. Test262's $262.IsHTMLDDA).
/// </summary>
public sealed class HtmlDdaValue : IIsHtmlDda, IJsCallable
{
    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        return null;
    }
}

internal interface IIsHtmlDda
{
}

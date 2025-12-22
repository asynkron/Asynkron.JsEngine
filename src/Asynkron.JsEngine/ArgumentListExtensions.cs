using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

internal static class ArgumentListExtensions
{
    public static JsValue GetArgument(this IReadOnlyList<JsValue> args, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return index < args.Count ? args[index] : JsValue.Undefined;
    }

    /// <summary>
    /// Returns a zero-copy slice starting at the given offset.
    /// </summary>
    public static ArgumentSlice SliceFrom(this IReadOnlyList<JsValue> args, int offset)
    {
        return args.Count > offset
            ? new ArgumentSlice(args, offset)
            : ArgumentSlice.Empty;
    }
}

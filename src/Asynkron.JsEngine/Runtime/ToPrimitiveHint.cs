namespace Asynkron.JsEngine.Runtime;

/// <summary>
/// Enum for ToPrimitive hint to avoid string comparisons in hot paths.
/// </summary>
internal enum ToPrimitiveHint
{
    Default,
    Number,
    String
}

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Exposes runtime caller metadata for functions that support the
///     non-standard <c>Function.prototype.caller</c> accessors (Annex B).
/// </summary>
internal interface ICallerInfo : IJsCallable
{
    /// <summary>
    ///     Most recent non-strict caller in the same realm, or <c>null</c>.
    /// </summary>
    IJsCallable? Caller { get; set; }

    /// <summary>
    ///     True when the function body is strict-mode (caller is then disallowed).
    /// </summary>
    bool IsStrictFunction { get; }
}

namespace Asynkron.JsEngine;

/// <summary>
///     Mutable implementation of <see cref="IJsEngineOptions" />.
/// </summary>
public sealed class JsEngineOptions : IJsEngineOptions
{
    /// <summary>
    ///     Default options used when none are provided.
    /// </summary>
    public static JsEngineOptions Default { get; } = new();

    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;
    public bool AllowImportMeta { get; init; } = true;
    public bool DebugMode { get; init; }
}

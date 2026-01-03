#region

using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Configurable options that control language features exposed by <see cref="JsEngine" />.
/// </summary>
public interface IJsEngineOptions
{
    /// <summary>
    ///     Time zone used for Date local-time calculations and formatting. Defaults to UTC to keep
    ///     evaluations deterministic across hosts; set to <see cref="TimeZoneInfo.Local" /> or any other zone to
    ///     emulate a specific environment.
    /// </summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>
    ///     Controls whether import.meta expressions are allowed. Per ES spec, import.meta is only
    ///     valid when the syntactic goal symbol is Module. Set to false when parsing in Script goal
    ///     (such as the Function constructor). Defaults to true.
    /// </summary>
    bool AllowImportMeta { get; }

    /// <summary>
    ///     Enables engine debug facilities such as trace channels and debug message capture. Defaults to false to avoid
    ///     allocating debug infrastructure when not needed.
    /// </summary>
    bool DebugMode { get; }

    /// <summary>
    ///     Enables script-level slot analysis and identifier caching when it is safe to do so
    ///     (no direct eval/with). Defaults to false for safety.
    /// </summary>
    bool AllowScriptSlotAnalysis { get; }

    /// <summary>
    ///     Optional logger used for realm-level diagnostics such as identifier slot hit/miss tracing.
    /// </summary>
    ILogger? Logger { get; }
}

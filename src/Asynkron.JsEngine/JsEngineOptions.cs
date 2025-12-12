namespace Asynkron.JsEngine;

/// <summary>
///     Configurable options that control language features exposed by <see cref="JsEngine" />.
/// </summary>
public interface IJsEngineOptions
{
    /// <summary>
    ///     Enables Annex B function declaration semantics in sloppy mode (block functions create
    ///     var-scoped bindings and leak into the containing scope). When disabled, block functions
    ///     remain block-scoped even in sloppy mode.
    /// </summary>
    bool EnableAnnexBFunctionExtensions { get; }

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
    ///     Enables a faster identifier read path that avoids per-access delegate allocations.
    ///     When enabled, identifier resolution is performed directly without creating
    ///     <see cref="Asynkron.JsEngine.Ast.AssignmentReference" /> instances.
    /// </summary>
    bool EnableFastIdentifierAccess { get; }

    /// <summary>
    ///     Enables faster property access paths for non-private properties to avoid allocating
    ///     <see cref="Asynkron.JsEngine.Ast.TypedAstEvaluator.PropertyHandle" /> objects in hot member reads/writes.
    /// </summary>
    bool EnableFastPropertyAccess { get; }
}

/// <summary>
///     Mutable implementation of <see cref="IJsEngineOptions" />.
/// </summary>
public sealed class JsEngineOptions : IJsEngineOptions
{
    /// <summary>
    ///     Default options used when none are provided.
    /// </summary>
    public static JsEngineOptions Default { get; } = new();

    public bool EnableAnnexBFunctionExtensions { get; init; } = true;
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;
    public bool AllowImportMeta { get; init; } = true;

    public bool EnableFastIdentifierAccess { get; init; } = true;

    public bool EnableFastPropertyAccess { get; init; } = true;
}

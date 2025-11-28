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
}

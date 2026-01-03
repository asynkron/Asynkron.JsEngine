using System;

namespace Asynkron.JsEngine;

/// <summary>
/// Centralized feature flags with kill switches for risky optimizations.
/// Defaults can be overridden via environment variables for quick rollback.
/// </summary>
internal static class EngineFeatureFlags
{
    private static readonly string? NestedStampingEnv =
        Environment.GetEnvironmentVariable("JSENGINE_ENABLE_NESTED_SLOT_STAMPING");
    private static readonly string? ThrowOnZeroScopeEnv =
        Environment.GetEnvironmentVariable("JSENGINE_THROW_ON_SCOPEID_ZERO");

    /// <summary>
    /// Controls whether nested function bodies are stamped with slot metadata.
    /// Default: true. Set JSENGINE_ENABLE_NESTED_SLOT_STAMPING=false to disable at runtime.
    /// </summary>
    internal static bool EnableNestedSlotStamping =>
        !string.Equals(NestedStampingEnv, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When true, any attempt to initialize a JsEnvironment with ScopeId == 0 throws.
    /// Use to surface missing scope analysis during development; default off for compatibility.
    /// </summary>
    internal static bool ThrowOnZeroScopeId =>
        string.Equals(ThrowOnZeroScopeEnv, "true", StringComparison.OrdinalIgnoreCase);
}

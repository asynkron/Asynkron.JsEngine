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
    private static readonly string? PreferSyncIrEnv =
        Environment.GetEnvironmentVariable("JSENGINE_PREFER_SYNC_IR");
    private static readonly string? ForceSyncIrEnv =
        Environment.GetEnvironmentVariable("JSENGINE_FORCE_SYNC_IR");

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

    /// <summary>
    /// When true, synchronous functions attempt IR execution first and fall back to the AST evaluator
    /// if plan generation fails. Default: false. Set JSENGINE_PREFER_SYNC_IR=true to enable at runtime.
    /// NOTE: Known issues with eval() in class field initializers when accessing super.
    /// </summary>
    internal static bool PreferSyncIr =>
        ForceSyncIr || string.Equals(PreferSyncIrEnv, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When true, synchronous functions MUST execute via IR. Plan generation failures surface as
    /// NotSupportedException (no AST fallback). Set JSENGINE_FORCE_SYNC_IR=true to enable at runtime.
    /// </summary>
    internal static bool ForceSyncIr =>
        string.Equals(ForceSyncIrEnv, "true", StringComparison.OrdinalIgnoreCase);
}

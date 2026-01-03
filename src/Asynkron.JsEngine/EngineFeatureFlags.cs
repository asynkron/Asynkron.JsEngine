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

    /// <summary>
    /// Controls whether nested function bodies are stamped with slot metadata.
    /// Default: true. Set JSENGINE_ENABLE_NESTED_SLOT_STAMPING=false to disable at runtime.
    /// </summary>
    internal static bool EnableNestedSlotStamping =>
        !string.Equals(NestedStampingEnv, "false", StringComparison.OrdinalIgnoreCase);
}

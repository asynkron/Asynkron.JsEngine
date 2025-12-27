namespace Asynkron.JsEngine;

/// <summary>
/// Global constants used throughout the JavaScript engine.
/// </summary>
public static class JsEngineConstants
{
    /// <summary>
    /// Maximum depth for prototype chain traversal to prevent infinite loops.
    /// Per ECMAScript specification, typical prototype chains are less than 10 deep.
    /// This limit ensures reasonable performance while preventing stack overflow
    /// from circular prototype references.
    /// </summary>
    public const int MaxPrototypeChainDepth = 100;

    public const bool SyncIrLoops = false;
}

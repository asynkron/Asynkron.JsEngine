using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// Global constants used throughout the JavaScript engine.
/// </summary>
/// <remarks>
/// Compile-time feature toggles (define in csproj or command line):
/// <list type="bullet">
///   <item>
///     <term>TRACE_IR_EXECUTION</term>
///     <description>Enable detailed IR execution tracing with environment depth indentation.
///     Very verbose - only enable for debugging specific issues.</description>
///   </item>
///   <item>
///     <term>DISABLE_POOLING</term>
///     <description>Disable all object pooling. Pool.Rent() always creates fresh instances,
///     Pool.Return() is a no-op. Use to establish a clean baseline for debugging.</description>
///   </item>
///   <item>
///     <term>NO_INLINING</term>
///     <description>Disable aggressive inlining. Use for profiling to get accurate call stacks.</description>
///   </item>
/// </list>
/// Example: dotnet build -p:DefineConstants=DISABLE_POOLING
/// </remarks>
public static class JsEngineConstants
{
    /// <summary>
    /// Maximum depth for prototype chain traversal to prevent infinite loops.
    /// Per ECMAScript specification, typical prototype chains are less than 10 deep.
    /// This limit ensures reasonable performance while preventing stack overflow
    /// from circular prototype references.
    /// </summary>
    public const int MaxPrototypeChainDepth = 100;

    /// <summary>
    /// Inlining strategy for hot path methods. Use with [MethodImpl(JsEngineConstants.Inlining)].
    /// Set to NoInlining when NO_INLINING is defined (useful for profiling).
    /// </summary>
#if NO_INLINING
    public const MethodImplOptions Inlining = MethodImplOptions.NoInlining;
#else
    public const MethodImplOptions Inlining = MethodImplOptions.AggressiveInlining;
#endif
}

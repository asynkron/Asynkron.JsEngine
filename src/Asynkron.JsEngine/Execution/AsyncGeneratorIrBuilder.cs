using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     IR builder for async generator functions.
///     For now async generators share the same IR surface as synchronous
///     generators; the difference is in how their iterator methods (`next`,
///     `throw`, `return`) are exposed to JavaScript (they return Promises). This
///     builder delegates to the synchronous generator IR builder so async
///     <c>function*</c> bodies benefit from the same instruction set and control
///     flow lowering as regular generators.
/// </summary>
internal static class AsyncGeneratorIrBuilder
{
    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan, out string? failureReason)
    {
        return SyncGeneratorIrBuilder.TryBuild(function, out plan, out failureReason);
    }
}

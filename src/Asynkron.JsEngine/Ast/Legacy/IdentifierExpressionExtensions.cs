#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue HandleIdentifierNotFound(Symbol name, EvaluationContext context)
    {
        var errorObject = StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState);
        context.SetThrow(errorObject);
        return errorObject;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateIdentifier(this IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        // `arguments` is an implicit binding; its slot isn't present in the analyzer's slot map,
        // so a cached slot hint can incorrectly point to an outer scope (e.g., a `var arguments`).
        // Always resolve it via normal binding lookup to ensure the per-call arguments object wins.
        if (ReferenceEquals(identifier.Name, Symbol.Arguments))
        {
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var argumentsValue))
            {
                return argumentsValue;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        if (!context.AllowIdentifierCache)
        {
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var value))
            {
                return value;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        if (environment.TryReadIdentifierWithSlot(identifier, context, out var slotValue))
        {
            return slotValue;
        }

        // Slow path: identifier not found - create proper error
        // Compiled out when TRACE_IR_EXECUTION not defined
        ExecutionPlanPrinter.TraceLookup(
            context.RealmState.Logger,
            identifier.Name.Name,
            false,
            environment.Depth,
            environment.ScopeId,
            environment.GetHashCode(),
            $"idScope={identifier.ScopeId} slot={identifier.SlotIndex}");
        return HandleIdentifierNotFound(identifier.Name, context);
    }
}

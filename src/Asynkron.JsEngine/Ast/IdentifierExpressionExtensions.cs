#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

#endregion

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

    extension(IdentifierExpression identifier)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateIdentifier(JsEnvironment environment,
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
#pragma warning disable CS0162 // Unreachable code detected (TraceIrExecution is compile-time constant)
            if (JsEngineConstants.TraceIrExecution && context.RealmState.Logger is not null)
            {
                ExecutionPlanPrinter.TraceLookup(
                    context.RealmState.Logger,
                    identifier.Name.Name,
                    false,
                    environment.Depth,
                    environment.ScopeId,
                    environment.GetHashCode(),
                    $"idScope={identifier.ScopeId} slot={identifier.SlotIndex}");
            }
#pragma warning restore CS0162
            return HandleIdentifierNotFound(identifier.Name, context);
        }
    }
}

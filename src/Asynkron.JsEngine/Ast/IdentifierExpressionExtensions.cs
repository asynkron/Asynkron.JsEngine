using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IdentifierExpression identifier)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateIdentifier(JsEnvironment environment,
            EvaluationContext context)
        {
            // Fast path: slot-based access using ScopeId to find the declaring environment.
            // This enables O(1) slot access for variables in any scope (local or closure).
            if (identifier.SlotIndex >= 0 && identifier.ScopeId >= 0)
            {
                // Fastest path: current environment matches (most common case for local variables)
                if (environment.ScopeId == identifier.ScopeId)
                {
                    var slots = environment._slots;
                    if (slots is not null)
                    {
                        return slots[identifier.SlotIndex];
                    }
                }
                else
                {
                    // Traverse up to find the environment with matching ScopeId
                    var targetEnv = environment.FindByScopeId(identifier.ScopeId);
                    if (targetEnv?._slots is not null)
                    {
                        return targetEnv._slots[identifier.SlotIndex];
                    }
                }
            }

            // Fallback: use dictionary-based lookup
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var value))
            {
                return value;
            }

            // Slow path: identifier not found - create proper error
            return HandleIdentifierNotFound(identifier.Name, environment, context);
        }
    }

    private static JsValue HandleIdentifierNotFound(Symbol name, JsEnvironment environment, EvaluationContext context)
    {
        var errorObject = StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState);
        context.SetThrow(JsValue.FromObject(errorObject));
        return JsValue.FromObject(errorObject);
    }
}

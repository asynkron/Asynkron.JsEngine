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
            // Fastest path: slot-based access when scope analysis resolved this identifier
            // to a local variable (ScopeDepth=0) AND the environment has slots initialized.
            // We only use slots for depth=0 because outer scopes (like global) may not have slots.
            var slots = environment._slots;
            if (identifier.SlotIndex >= 0 && identifier.ScopeDepth == 0 && slots is not null)
            {
                return slots[identifier.SlotIndex];
            }

            // Fast path: use TryGetIdentifierJsValue to avoid exception overhead
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

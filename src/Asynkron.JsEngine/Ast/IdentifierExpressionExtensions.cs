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
            // AND the environment has slots initialized
            if (identifier.SlotIndex >= 0 && environment.HasSlots)
            {
                return environment.GetSlot(identifier.ScopeDepth, identifier.SlotIndex);
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

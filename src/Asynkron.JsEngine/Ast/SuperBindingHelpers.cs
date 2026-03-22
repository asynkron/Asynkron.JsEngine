#region

using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Assigns a value to a super property, handling all validation and strict mode checks.
    /// </summary>
    private static JsValue AssignToSuperBinding(
        JsEnvironment environment,
        EvaluationContext context,
        string propertyName,
        JsValue value,
        string logDescription)
    {
        var binding = environment.ExpectSuperBinding(context);
        environment.RealmState?.Logger?.LogInformation(
            "SuperBinding: assign super {Description} protoNull={ProtoNull} thisInit={ThisInit}",
            logDescription,
            binding.Prototype is null,
            binding.IsThisInitialized);

        if (!binding.IsThisInitialized)
        {
            throw environment.CreateSuperReferenceError(context);
        }

        if (binding.Prototype is null)
        {
            throw StandardLibrary.ThrowTypeError(
                "Cannot assign to super property when prototype is null or undefined.",
                context,
                context.RealmState);
        }

        // Per ES spec 6.2.3.2 PutValue, if set fails in strict mode, throw TypeError
        if (!binding.TrySetProperty(propertyName, value, out _) &&
            environment.IsStrict)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot assign to read only property '{propertyName}' of object",
                context,
                context.RealmState);
        }

        return value;
    }
}

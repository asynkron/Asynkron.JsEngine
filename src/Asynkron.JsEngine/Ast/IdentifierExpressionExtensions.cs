using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IdentifierExpression identifier)
    {
        private object? EvaluateIdentifier(JsEnvironment environment,
            EvaluationContext context)
        {
            var reference = AssignmentReferenceResolver.Resolve(identifier, environment, context, EvaluateExpression);
            try
            {
                return reference.GetValue();
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
            {
                object? errorObject = ex.Message;

                if (environment.TryGet(Symbol.ReferenceErrorIdentifier, out var ctor) &&
                    ctor is IJsCallable callable)
                {
                    try
                    {
                        errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
                    }
                    catch (ThrowSignal signal)
                    {
                        errorObject = signal.ThrownValue;
                    }
                }

                context.SetThrow(errorObject);
                return errorObject;
            }
        }
    }
}

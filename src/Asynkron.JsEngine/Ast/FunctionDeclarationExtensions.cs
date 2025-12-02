namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(FunctionDeclaration declaration)
    {
        private object? EvaluateFunctionDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            // FunctionDeclarationInstantiation handles creating and binding functions up front.
            // Runtime evaluation is a no-op (NormalCompletion(empty)).
            return EmptyCompletion;
        }
    }
}

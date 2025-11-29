namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(IfStatement statement)
    {
        private object? EvaluateIf(JsEnvironment environment, EvaluationContext context)
        {
            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var branch = IsTruthy(test) ? statement.Then : statement.Else;
            if (branch is null)
            {
                return Symbol.Undefined;
            }

            if (branch is BlockStatement block)
            {
                return EvaluateBlock(block, environment, context);
            }

            var branchScope = new JsEnvironment(environment, false, context.CurrentScope.IsStrict);
            return EvaluateStatement(branch, branchScope, context);
        }
    }

}

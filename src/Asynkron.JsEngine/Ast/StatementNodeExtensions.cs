using System;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(StatementNode statement)
    {
        private object? EvaluateStatement(
            JsEnvironment environment,
            EvaluationContext context,
            Symbol? activeLabel = null)
        {
            context.SourceReference = statement.Source;
            context.ThrowIfCancellationRequested();

            return statement switch
            {
                BlockStatement block => EvaluateBlock(block, environment, context),
                ExpressionStatement expressionStatement => EvaluateExpression(expressionStatement.Expression, environment,
                    context),
                ReturnStatement returnStatement => EvaluateReturn(returnStatement, environment, context),
                ThrowStatement throwStatement => EvaluateThrow(throwStatement, environment, context),
                VariableDeclaration declaration => EvaluateVariableDeclaration(declaration, environment, context),
                FunctionDeclaration functionDeclaration => EvaluateFunctionDeclaration(functionDeclaration, environment,
                    context),
                IfStatement ifStatement => EvaluateIf(ifStatement, environment, context),
                WhileStatement whileStatement => EvaluateWhile(whileStatement, environment, context, activeLabel),
                DoWhileStatement doWhileStatement => EvaluateDoWhile(doWhileStatement, environment, context, activeLabel),
                ForStatement forStatement => EvaluateFor(forStatement, environment, context, activeLabel),
                ForEachStatement forEachStatement => EvaluateForEach(forEachStatement, environment, context, activeLabel),
                BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
                ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
                LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
                TryStatement tryStatement => EvaluateTry(tryStatement, environment, context),
                SwitchStatement switchStatement => EvaluateSwitch(switchStatement, environment, context, activeLabel),
                ClassDeclaration classDeclaration => EvaluateClass(classDeclaration, environment, context),
                WithStatement withStatement => EvaluateWith(withStatement, environment, context),
                EmptyStatement => EmptyCompletion,
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{statement.GetType().Name}'.")
            };
        }
    }
}

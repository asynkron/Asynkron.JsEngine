#region

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

#endregion

namespace Asynkron.JsEngine.Ast.ShapeAnalyzer;

internal static class AstShapeAnalyzer
{
    public static ShapeSummary AnalyzeExpression(ExpressionNode? expression, bool includeNestedFunctions = false)
    {
        var counter = new ShapeCounter(includeNestedFunctions);
        counter.VisitExpression(expression);
        return new ShapeSummary(
            counter.YieldCount,
            counter.DelegatedYieldCount,
            counter.AwaitCount,
            counter.YieldOperandContainsYield);
    }

    public static ShapeSummary AnalyzeStatement(StatementNode statement, bool includeNestedFunctions = false)
    {
        var counter = new ShapeCounter(includeNestedFunctions);
        counter.VisitStatement(statement);
        return new ShapeSummary(
            counter.YieldCount,
            counter.DelegatedYieldCount,
            counter.AwaitCount,
            counter.YieldOperandContainsYield);
    }

    public static bool ContainsYield([NotNullWhen(true)] ExpressionNode? expression,
        bool includeNestedFunctions = false)
    {
        return AnalyzeExpression(expression, includeNestedFunctions).HasYield;
    }

    public static bool ContainsAwait(ExpressionNode? expression, bool includeNestedFunctions = false)
    {
        return AnalyzeExpression(expression, includeNestedFunctions).HasAwait;
    }

    public static bool StatementContainsYield(StatementNode statement, bool includeNestedFunctions = false)
    {
        return AnalyzeStatement(statement, includeNestedFunctions).HasYield;
    }

    public static bool StatementContainsAwait(StatementNode statement, bool includeNestedFunctions = false)
    {
        return AnalyzeStatement(statement, includeNestedFunctions).HasAwait;
    }

    /// <summary>
    /// Checks if a binding target contains yield expressions in any of its default values.
    /// Used by GeneratorYieldLowerer and DeclarationEmitter.
    /// </summary>
    public static bool BindingTargetContainsYieldInDefaultValue(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultValue is not null && ContainsYield(element.DefaultValue))
                    {
                        return true;
                    }

                    if (element.Target is not null && BindingTargetContainsYieldInDefaultValue(element.Target))
                    {
                        return true;
                    }
                }

                return arrayBinding.RestElement is not null &&
                    BindingTargetContainsYieldInDefaultValue(arrayBinding.RestElement);

            case ObjectBinding objectBinding:
                foreach (var prop in objectBinding.Properties)
                {
                    if (prop.DefaultValue is not null && ContainsYield(prop.DefaultValue))
                    {
                        return true;
                    }

                    if (BindingTargetContainsYieldInDefaultValue(prop.Target))
                    {
                        return true;
                    }
                }

                return objectBinding.RestElement is not null &&
                    BindingTargetContainsYieldInDefaultValue(objectBinding.RestElement);

            case AssignmentTargetBinding assignmentTarget:
                return ContainsYield(assignmentTarget.Expression);

            default:
                return false;
        }
    }

    public static bool TryFindSingleYield(ExpressionNode expression,
        [NotNullWhen(true)] out YieldExpression? yieldExpression)
    {
        var summary = AnalyzeExpression(expression);
        if (summary.YieldCount != 1)
        {
            yieldExpression = null!;
            return false;
        }

        var locator = new SingleYieldLocator();
        locator.VisitExpression(expression);
        yieldExpression = locator.FoundYield;
        return yieldExpression is not null;
    }

    public static bool TryRewriteSingleYield(
        ExpressionNode expression,
        Symbol replacementSymbol,
        [NotNullWhen(true)] out YieldExpression? yieldExpression,
        [NotNullWhen(true)] out ExpressionNode? rewritten)
    {
        var summary = AnalyzeExpression(expression);
        if (summary.YieldCount != 1)
        {
            yieldExpression = null;
            rewritten = expression;
            return false;
        }

        var rewriter = new SingleYieldRewriter(replacementSymbol);
        rewritten = rewriter.Rewrite(expression);
        yieldExpression = rewriter.FoundYield;
        return yieldExpression is not null;
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct ShapeSummary(
        int YieldCount,
        int DelegatedYieldCount,
        int AwaitCount,
        bool YieldOperandContainsYield)
    {
        public bool HasYield => YieldCount > 0;
        public bool HasAwait => AwaitCount > 0;
    }
}

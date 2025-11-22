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

    public static bool ContainsYield(ExpressionNode? expression, bool includeNestedFunctions = false)
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

    public static bool TryFindSingleYield(ExpressionNode expression, out YieldExpression yieldExpression)
    {
        var summary = AnalyzeExpression(expression);
        if (summary.YieldCount != 1)
        {
            yieldExpression = null!;
            return false;
        }

        var locator = new SingleYieldLocator();
        locator.VisitExpression(expression);
        yieldExpression = locator.FoundYield!;
        return yieldExpression is not null;
    }

    public static bool TryRewriteSingleYield(
        ExpressionNode expression,
        Symbol replacementSymbol,
        out YieldExpression yieldExpression,
        out ExpressionNode rewritten)
    {
        var summary = AnalyzeExpression(expression);
        if (summary.YieldCount != 1)
        {
            yieldExpression = null!;
            rewritten = expression;
            return false;
        }

        var rewriter = new SingleYieldRewriter(replacementSymbol);
        rewritten = rewriter.Rewrite(expression);
        yieldExpression = rewriter.FoundYield!;
        return yieldExpression is not null;
    }

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

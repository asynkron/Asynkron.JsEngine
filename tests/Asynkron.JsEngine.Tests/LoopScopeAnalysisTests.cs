using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Tests;

public class LoopScopeAnalysisTests
{
    [Fact]
    public void ForLoopWithLetInitializer_TracksPerIterationSlotsAndPlanBindings()
    {
        const string source = """
            let total = 0;
            for (let i = 0, s = 0; i < 3; i++) {
                total += i;
                s = s + i;
            }
            total;
            """;

        var (analyzed, afterCps) = ParseWithPipeline(source);
        var forStatement = FindFirst<ForStatement>(analyzed);

        Assert.True(forStatement.PerIterationScopeId >= 0);
        Assert.Equal(2, forStatement.PerIterationSlotCount);
        Assert.Equal(new[] { 0, 1 }, forStatement.PerIterationSlotIndices.ToArray());

        var plan = ((IAstCacheable<LoopPlan>)forStatement).GetOrCreateCache();
        Assert.Equal(forStatement.PerIterationScopeId, plan.IterationScopeId);
        Assert.Equal(forStatement.PerIterationSlotCount, plan.IterationSlotCount);
        Assert.Equal(forStatement.PerIterationSlotIndices, plan.PerIterationSlotIndices);
        Assert.Equal(new[] { "i", "s" }, plan.PerIterationBindings.Select(b => b.Name).ToArray());

        var forStatementAfterCps = FindFirst<ForStatement>(afterCps);
        Assert.Equal(forStatement.PerIterationScopeId, forStatementAfterCps.PerIterationScopeId);
        Assert.Equal(forStatement.PerIterationSlotCount, forStatementAfterCps.PerIterationSlotCount);
        Assert.Equal(forStatement.PerIterationSlotIndices, forStatementAfterCps.PerIterationSlotIndices);
    }

    [Fact]
    public void ForInWithLetBinding_PropagatesPerIterationMetadataToIteratorPlan()
    {
        const string source = """
            let obj = { a: 1, b: 2, c: 3 };
            for (let key in obj) {
                obj[key];
            }
            """;

        var (analyzed, afterCps) = ParseWithPipeline(source);
        var forEach = FindFirst<ForEachStatement>(analyzed);

        Assert.Equal(ForEachKind.In, forEach.Kind);
        Assert.True(forEach.PerIterationScopeId >= 0);
        Assert.Equal(1, forEach.PerIterationSlotCount);
        Assert.Equal(new[] { 0 }, forEach.PerIterationSlotIndices.ToArray());
        Assert.Equal(new[] { "key" }, forEach.PerIterationBindings.Select(b => b.Name).ToArray());

        var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.Equal(forEach.PerIterationScopeId, plan.IterationScopeId);
        Assert.Equal(forEach.PerIterationSlotCount, plan.IterationSlotCount);
        Assert.Equal(forEach.PerIterationSlotIndices, plan.PerIterationSlotIndices);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), plan.PerIterationBindings.Select(b => b.Name));

        var forEachAfterCps = FindFirst<ForEachStatement>(afterCps);
        Assert.Equal(forEach.PerIterationScopeId, forEachAfterCps.PerIterationScopeId);
        Assert.Equal(forEach.PerIterationSlotCount, forEachAfterCps.PerIterationSlotCount);
        Assert.Equal(forEach.PerIterationSlotIndices, forEachAfterCps.PerIterationSlotIndices);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), forEachAfterCps.PerIterationBindings.Select(b => b.Name));
    }

    [Fact]
    public void ForOfWithDestructuringBinding_PreservesSlotOrdering()
    {
        const string source = """
            const pairs = [[1, 2], [3, 4]];
            for (let [x, y] of pairs) {
                x + y;
            }
            """;

        var (analyzed, afterCps) = ParseWithPipeline(source);
        var forEach = FindFirst<ForEachStatement>(analyzed);

        Assert.Equal(ForEachKind.Of, forEach.Kind);
        Assert.True(forEach.PerIterationScopeId >= 0);
        Assert.Equal(2, forEach.PerIterationSlotCount);
        Assert.Equal(new[] { 0, 1 }, forEach.PerIterationSlotIndices.ToArray());
        Assert.Equal(new[] { "x", "y" }, forEach.PerIterationBindings.Select(b => b.Name).ToArray());

        var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.Equal(forEach.PerIterationScopeId, plan.IterationScopeId);
        Assert.Equal(forEach.PerIterationSlotCount, plan.IterationSlotCount);
        Assert.Equal(forEach.PerIterationSlotIndices, plan.PerIterationSlotIndices);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), plan.PerIterationBindings.Select(b => b.Name));

        var forEachAfterCps = FindFirst<ForEachStatement>(afterCps);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), forEachAfterCps.PerIterationBindings.Select(b => b.Name));
        Assert.Equal(forEach.PerIterationSlotIndices, forEachAfterCps.PerIterationSlotIndices);
    }

    private static (ProgramNode analyzed, ProgramNode afterCps) ParseWithPipeline(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new TypedAstParser(tokens, source);
        var program = parser.ParseProgram();

        var constant = new TypedConstantExpressionTransformer().Transform(program);
        var analyzed = new ScopeAnalyzer().Analyze(constant);
        var cpsTransformed = TypedCpsTransformer.NeedsTransformation(analyzed)
            ? new TypedCpsTransformer().Transform(analyzed)
            : analyzed;

        return (analyzed, cpsTransformed);
    }

    private static T FindFirst<T>(ProgramNode program) where T : class
    {
        foreach (var statement in program.Body)
        {
            var match = FindInStatement<T>(statement);
            if (match is not null)
            {
                return match;
            }
        }

        throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} found in program.");
    }

    private static T? FindInStatement<T>(StatementNode statement) where T : class
    {
        if (statement is T typed)
        {
            return typed;
        }

        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    var found = FindInStatement<T>(child);
                    if (found is not null)
                    {
                        return found;
                    }
                }
                break;
            case IfStatement ifStatement:
                var inThen = FindInStatement<T>(ifStatement.Then);
                if (inThen is not null)
                {
                    return inThen;
                }
                if (ifStatement.Else is not null)
                {
                    var inElse = FindInStatement<T>(ifStatement.Else);
                    if (inElse is not null)
                    {
                        return inElse;
                    }
                }
                break;
            case ForStatement forStatement:
                return FindInStatement<T>(forStatement.Body);
            case ForEachStatement forEachStatement:
                return FindInStatement<T>(forEachStatement.Body);
            case WhileStatement whileStatement:
                return FindInStatement<T>(whileStatement.Body);
            case DoWhileStatement doWhileStatement:
                return FindInStatement<T>(doWhileStatement.Body);
            case LabeledStatement labeledStatement:
                return FindInStatement<T>(labeledStatement.Statement);
            case TryStatement tryStatement:
                var inTry = FindInStatement<T>(tryStatement.TryBlock);
                if (inTry is not null)
                {
                    return inTry;
                }
                if (tryStatement.Catch is not null)
                {
                    var inCatch = FindInStatement<T>(tryStatement.Catch.Body);
                    if (inCatch is not null)
                    {
                        return inCatch;
                    }
                }
                if (tryStatement.Finally is not null)
                {
                    return FindInStatement<T>(tryStatement.Finally);
                }
                break;
            case SwitchStatement switchStatement:
                foreach (var switchCase in switchStatement.Cases)
                {
                    var inCase = FindInStatement<T>(switchCase.Body);
                    if (inCase is not null)
                    {
                        return inCase;
                    }
                }
                break;
        }

        return null;
    }
}

using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class AstVisitorDispatchTests
{
    [Fact]
    public void FunctionDeclarationBodyIsVisited()
    {
        const string source = """
            function outer() {
                let captured = 41;
                function inner() { return captured + 1; }
                return inner();
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var visitor = new RecordingVisitor();

        foreach (var statement in pipeline.Analyzed.Body)
        {
            visitor.Visit(statement);
        }

        Assert.Contains("captured", visitor.Identifiers);
    }

    [Fact]
    public void BlockVisitorOverrideRunsForNestedBlocks()
    {
        const string source = """
            function outer() {
                {
                    let shadow = 1;
                    function inner() { return shadow; }
                }
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var visitor = new RecordingVisitor();

        foreach (var statement in pipeline.Analyzed.Body)
        {
            visitor.Visit(statement);
        }

        // outer body + nested block + inner function body
        Assert.True(visitor.BlockVisits >= 3);
    }

    private sealed class RecordingVisitor : AstVisitor
    {
        public List<string> Identifiers { get; } = [];
        public int BlockVisits { get; private set; }

        protected override void VisitIdentifierExpression(IdentifierExpression node)
        {
            Identifiers.Add(node.Name.Name);
        }

        protected override void VisitBlockStatement(BlockStatement node)
        {
            BlockVisits++;
            base.VisitBlockStatement(node);
        }
    }
}

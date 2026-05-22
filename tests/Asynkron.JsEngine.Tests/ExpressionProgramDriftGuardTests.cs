using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
public sealed class ExpressionProgramDriftGuardTests
{
    [Fact]
    public void SupportedExpressionSamples_DoNotRegressToUnsupportedExpressionProgram()
    {
        var probes = new[]
        {
            new Probe("literal", "42"),
            new Probe("identifier", "value"),
            new Probe("binary", "left + right"),
            new Probe("conditional", "flag ? left : right"),
            new Probe("sequence", "(left, right, left + right)"),
            new Probe("member-dot", "box.value"),
            new Probe("member-index", "box[dynamicPropertyName]"),
            new Probe("simple-call", "fn(left)"),
            new Probe("template-literal", "`sum=${left + right}`"),
            new Probe("object-literal", "({ value: left, ['computed']: right })")
        };

        foreach (var probe in probes)
        {
            var buildResult = BuildProbeResult(probe.ExpressionSource);

            Assert.True(
                buildResult.Succeeded,
                $"Supported probe '{probe.Name}' regressed. FailureCode={buildResult.Failure?.Code}, " +
                $"ExpressionFailureCode={buildResult.Failure?.ExpressionFailureCode}, Detail={buildResult.FailureReason}");
        }
    }

    [Fact]
    public void UnsupportedExpressionAllowlist_StaysExplicitWithExpectedFailureCodes()
    {
        var probes = new[]
        {
            new UnsupportedProbe(
                "direct member call with non-dot-access property expression",
                new CallExpression(
                    null,
                    new MemberExpression(
                        null,
                        new IdentifierExpression(null, Symbol.Intern("box")),
                        new SequenceExpression(
                            null,
                            new IdentifierExpression(null, Symbol.Intern("left")),
                            new IdentifierExpression(null, Symbol.Intern("right"))),
                        IsComputed: false,
                        IsOptional: false),
                    [],
                    IsOptional: false),
                ExpressionProgramFailureCode.UnsupportedDirectMemberCallPropertyName)
        };

        foreach (var probe in probes)
        {
            var buildResult = BuildProbeResult(probe.Expression);

            Assert.False(buildResult.Succeeded, $"Unsupported probe '{probe.Name}' unexpectedly succeeded.");
            Assert.NotNull(buildResult.Failure);
            Assert.Equal(ExecutionPlanFailureCode.UnsupportedExpressionProgram, buildResult.Failure!.Code);
            Assert.Equal(probe.ExpectedFailureCode, buildResult.Failure.ExpressionFailureCode);
        }
    }

    [Fact]
    public void DirectMemberCall_StaticIdentifierPropertyNode_IsSupported()
    {
        var expression = new CallExpression(
            null,
            new MemberExpression(
                null,
                new IdentifierExpression(null, Symbol.Intern("box")),
                new IdentifierExpression(null, Symbol.Intern("read")),
                IsComputed: false,
                IsOptional: false),
            [],
            IsOptional: false);

        var buildResult = BuildProbeResult(expression);

        Assert.True(
            buildResult.Succeeded,
            $"Static identifier direct member call regressed. FailureCode={buildResult.Failure?.Code}, " +
            $"ExpressionFailureCode={buildResult.Failure?.ExpressionFailureCode}, Detail={buildResult.FailureReason}");
    }

    private static ExecutionPlanBuildResult BuildProbeResult(string expressionSource)
    {
        var expression = ParseReturnExpression(expressionSource);
        return BuildProbeResult(expression);
    }

    private static ExecutionPlanBuildResult BuildProbeResult(ExpressionNode expression)
    {
        var program = AstTestHelpers.ParseAndAnalyze("""
            function probe(value, left, right, flag, box, fn, dynamicPropertyName) {
                return value;
            }
            """);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(program.Analyzed.Body));
        var function = declaration.Function;
        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(function.Body.Statements));
        var mutatedReturnStatement = returnStatement with { Expression = expression };
        var mutatedBody = function.Body with { Statements = [mutatedReturnStatement] };
        var mutatedFunction = function with { Body = mutatedBody };
        return ExecutionPlanBuilder.Build(mutatedFunction);
    }

    private static ExpressionNode ParseReturnExpression(string expressionSource)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze($$"""
            function probe(value, left, right, flag, box, fn, dynamicPropertyName) {
                return {{expressionSource}};
            }
            """);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(pipeline.Analyzed.Body));
        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(declaration.Function.Body.Statements));
        Assert.NotNull(returnStatement.Expression);
        return returnStatement.Expression;
    }

    private sealed record Probe(string Name, string ExpressionSource);

    private sealed record UnsupportedProbe(
        string Name,
        ExpressionNode Expression,
        ExpressionProgramFailureCode ExpectedFailureCode);
}

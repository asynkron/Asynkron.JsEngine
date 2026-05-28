using System.Reflection;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.UnifiedBytecode;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
public sealed class ExpressionProgramCoverageMapTests
{
    private static readonly Regex MapEntryPattern = new(
        "^\\| `(?<name>[A-Za-z0-9_]+)` \\|",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void CoverageMap_ListsEveryConcreteExpressionNodeType()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coverageMapPath = Path.Combine(repositoryRoot.FullName, "docs", "expression-bytecode-coverage.md");

        Assert.True(File.Exists(coverageMapPath), $"Expected coverage map at '{coverageMapPath}'.");

        var mapText = File.ReadAllText(coverageMapPath);
        var documentedTypes = MapEntryPattern
            .Matches(mapText)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var expressionTypes = typeof(ExpressionNode).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(type => typeof(ExpressionNode).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToArray();

        var missing = expressionTypes
            .Where(typeName => !documentedTypes.Contains(typeName))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Coverage map is missing expression nodes: {string.Join(", ", missing)}");

        Assert.Contains("## Source-Of-Truth Surfaces", mapText, StringComparison.Ordinal);
        Assert.Contains("## ExpressionOpKind Capability Inventory", mapText, StringComparison.Ordinal);
        Assert.Contains("## Expression Family And Risk Groups", mapText, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var contractText = File.ReadAllText(contractPath);
        Assert.Contains("## Source-Of-Truth Surfaces", contractText, StringComparison.Ordinal);
        Assert.Contains("## No-Mixed-Execution Rule", contractText, StringComparison.Ordinal);
        Assert.Contains("## Current Support Matrix", contractText, StringComparison.Ordinal);
        Assert.Contains("## Reserved Ownership Lanes (planned, not implemented)", contractText, StringComparison.Ordinal);
        Assert.Contains("## Proof Commands", contractText, StringComparison.Ordinal);

        foreach (var opcodeName in Enum.GetNames<UnifiedBytecodeOpCode>())
        {
            Assert.Contains($"`{opcodeName}`", contractText, StringComparison.Ordinal);
        }

        foreach (var declineCodeName in Enum.GetNames<UnifiedBytecodeProductionDeclineCode>())
        {
            Assert.Contains($"`{declineCodeName}`", contractText, StringComparison.Ordinal);
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Asynkron.JsEngine.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}

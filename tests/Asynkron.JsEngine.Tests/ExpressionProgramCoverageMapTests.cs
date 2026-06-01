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
    private static readonly Regex ContractLedgerRowPattern = new(
        "^\\| `(?<key>[A-Za-z0-9_:-]+)` \\|",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly string[] ProductionPreGateLedgerKeys =
    [
        "pre-gate:newTarget",
        "pre-gate:IsClassConstructor",
        "pre-gate:IsArrowFunction",
        "pre-gate:IsAsyncLike",
        "pre-gate:IsGenerator",
        "pre-gate:IsDefaultDerivedConstructor",
        "pre-gate:hasParameterExpressions",
        "pre-gate:hasOnlySimpleIdentifierParameters",
        "pre-gate:usesArguments",
        "pre-gate:needsArgumentsBinding",
        "pre-gate:functionDeclarationsOrParameterVar",
        "pre-gate:allowIdentifierCache",
        "pre-gate:lexicalThisEnvironment",
        "pre-gate:PrivateNameScope",
        "pre-gate:capturedPrivateNameScopes",
        "pre-gate:superConstructor",
        "pre-gate:superPrototype",
        "pre-gate:instanceFields",
        "pre-gate:functionNameParameterConflict",
        "pre-gate:activationSlotShape"
    ];

    private static readonly string[] ProductionPrototypeGuardLedgerKeys =
    [
        "prototype-guard:Binary",
        "prototype-guard:Jump",
        "prototype-guard:JumpIfFalse",
        "prototype-guard:DefaultUnsupportedOpcode"
    ];

    private sealed record ProductionUnifiedBytecodeProofPackShape(
        string Key,
        string ContractEvidenceText,
        string SelectorAndOpcodeTest,
        string VmBehaviorTest,
        string PublicRouteHitTest,
        string NearbyNoRouteTest,
        string NoMixedExecutionSourceGate);

    private static readonly ProductionUnifiedBytecodeProofPackShape[] ProductionUnifiedBytecodeProofPackShapes =
    [
        new(
            "ordinary-sync:linear-slot-literal-return",
            "function passThrough(x) { var y = x; return y; }",
            "Evaluate_LinearSlotLiteralReturnPlan_Accepts",
            "Execute_LinearSlotLiteralReturnPlan_ReturnsSlotValueInProductionVm",
            "LinearSlotReturnFunction_UsesUnifiedBytecodeProductionFastPath",
            "NestedFunctionDeclaration_DeclinesUnifiedBytecodeProductionFastPath",
            "SourceGate_ProductionUnifiedBytecodeAcceptedPath_DoesNotDelegateToAstOrExecutionPlanRunner")
    ];

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
        Assert.Contains("## Checked Production Decline Ledger", contractText, StringComparison.Ordinal);
        Assert.Contains("### Eligibility Decline Rows", contractText, StringComparison.Ordinal);
        Assert.Contains("### Sync Production Pre-Gate Rows", contractText, StringComparison.Ordinal);
        Assert.Contains("### Prototype Opcode Guard Rows", contractText, StringComparison.Ordinal);
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

        var ledgerKeys = ContractLedgerRowPattern
            .Matches(contractText)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var declineCodeName in Enum.GetNames<UnifiedBytecodeProductionDeclineCode>())
        {
            Assert.Contains(declineCodeName, ledgerKeys);
        }

        foreach (var preGateKey in ProductionPreGateLedgerKeys)
        {
            Assert.Contains(preGateKey, ledgerKeys);
        }

        foreach (var prototypeGuardKey in ProductionPrototypeGuardLedgerKeys)
        {
            Assert.Contains(prototypeGuardKey, ledgerKeys);
        }
    }

    [Fact]
    public void UnifiedBytecodeProductionProofPack_CoversAdmittedOrdinarySyncBaseline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        var eligibilityTestsPath = Path.Combine(
            repositoryRoot.FullName,
            "tests",
            "Asynkron.JsEngine.Tests",
            "UnifiedBytecodeProductionEligibilityTests.cs");
        var invocationTestsPath = Path.Combine(
            repositoryRoot.FullName,
            "tests",
            "Asynkron.JsEngine.Tests",
            "UnifiedBytecodeProductionInvocationTests.cs");

        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");
        Assert.True(File.Exists(eligibilityTestsPath), $"Expected eligibility tests at '{eligibilityTestsPath}'.");
        Assert.True(File.Exists(invocationTestsPath), $"Expected invocation tests at '{invocationTestsPath}'.");
        Assert.NotEmpty(ProductionUnifiedBytecodeProofPackShapes);

        var contractText = File.ReadAllText(contractPath);
        var eligibilityTests = File.ReadAllText(eligibilityTestsPath);
        var invocationTests = File.ReadAllText(invocationTestsPath);

        foreach (var shape in ProductionUnifiedBytecodeProofPackShapes)
        {
            Assert.Contains(shape.ContractEvidenceText, contractText, StringComparison.Ordinal);
            AssertMethodExists(eligibilityTests, shape.SelectorAndOpcodeTest, shape.Key);
            AssertMethodExists(eligibilityTests, shape.VmBehaviorTest, shape.Key);
            AssertMethodExists(invocationTests, shape.PublicRouteHitTest, shape.Key);
            AssertMethodExists(invocationTests, shape.NearbyNoRouteTest, shape.Key);
            AssertMethodExists(invocationTests, shape.NoMixedExecutionSourceGate, shape.Key);
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

    private static void AssertMethodExists(string sourceText, string methodName, string shapeKey)
    {
        var pattern = new Regex(
            $@"\bpublic\s+(?:async\s+)?(?:Task|void)\s+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.CultureInvariant);

        Assert.True(
            pattern.IsMatch(sourceText),
            $"Production unified-bytecode proof pack shape '{shapeKey}' is missing proof method '{methodName}'.");
    }
}

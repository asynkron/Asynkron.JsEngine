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

    private static readonly string[] UnifiedBytecodeCompilerDeclinedInstructionNames =
    [
        "BindingVariableDeclarationInstruction",
        "ClassDeclarationInstruction",
        "FunctionDeclarationInstruction"
    ];

    private static readonly string[] UnifiedBytecodeExpressionOpCompilerGapNames =
    [
        "DefineComputedObjectAccessor",
        "DefineComputedObjectMethod",
        "DefineObjectAccessor",
        "DefineObjectMethod",
        "GetComputedSuperProperty",
        "GetNamedSuperProperty",
        "LoadTemplateObject",
        "PrivateFieldIn",
        "SetComputedSuperProperty",
        "SetNamedSuperProperty",
        "UpdateComputedSuperProperty",
        "UpdateNamedSuperProperty"
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

        var declineCodeNames = Enum.GetNames<UnifiedBytecodeProductionDeclineCode>();
        foreach (var declineCodeName in declineCodeNames)
        {
            Assert.Contains($"`{declineCodeName}`", contractText, StringComparison.Ordinal);
        }

        var documentedDeclineCodeNames = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### Production Decline Families (current)");
        AssertSameSet(
            declineCodeNames,
            documentedDeclineCodeNames,
            "Production decline family inventory");

        var ledgerKeys = ContractLedgerRowPattern
            .Matches(contractText)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var expectedLedgerKeys = declineCodeNames
            .Concat(ProductionPreGateLedgerKeys)
            .Concat(ProductionPrototypeGuardLedgerKeys);
        AssertSameSet(expectedLedgerKeys, ledgerKeys, "Production decline ledger keys");
    }

    [Fact]
    public void UnifiedBytecodeVirtualMachine_HandlesEveryDeclaredOpcode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var virtualMachinePath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeVirtualMachine.cs");
        Assert.True(File.Exists(virtualMachinePath), $"Expected VM source at '{virtualMachinePath}'.");

        var virtualMachineText = File.ReadAllText(virtualMachinePath);
        var handledOpcodes = ExtractUnifiedBytecodeOpcodeCases(virtualMachineText);

        AssertSameSet(Enum.GetNames<UnifiedBytecodeOpCode>(), handledOpcodes, "Unified bytecode VM opcode cases");
    }

    [Fact]
    public void UnifiedBytecodeCompiler_HandlesEveryDeclaredInstructionExceptDocumentedDeclines()
    {
        var repositoryRoot = FindRepositoryRoot();
        var instructionsPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "Instructions",
            "Instructions.cs");
        var compilerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeCompiler.cs");
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(instructionsPath), $"Expected instruction source at '{instructionsPath}'.");
        Assert.True(File.Exists(compilerPath), $"Expected compiler source at '{compilerPath}'.");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var instructionsText = File.ReadAllText(instructionsPath);
        var compilerText = File.ReadAllText(compilerPath);
        var contractText = File.ReadAllText(contractPath);
        var declaredInstructions = ExtractExecutionInstructionRecordNames(instructionsText);
        var tryCompileBlockText = ExtractSourceSection(
            compilerText,
            "private static bool TryCompileBlock(",
            "private static bool TryCompileTarget(");
        var compiledInstructionCases = ExtractExecutionInstructionCases(tryCompileBlockText);
        var expectedCompilerCases = declaredInstructions.Except(
            UnifiedBytecodeCompilerDeclinedInstructionNames,
            StringComparer.Ordinal);

        AssertSameSet(expectedCompilerCases, compiledInstructionCases, "Unified bytecode compiler instruction cases");
        foreach (var declinedInstructionName in UnifiedBytecodeCompilerDeclinedInstructionNames)
        {
            Assert.Contains($"`{declinedInstructionName}`", contractText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnifiedBytecodeCompiler_AccountsForEveryExpressionOpKind()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expressionOpPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "Instructions",
            "ExpressionOp.cs");
        var compilerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeCompiler.cs");
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(expressionOpPath), $"Expected expression op source at '{expressionOpPath}'.");
        Assert.True(File.Exists(compilerPath), $"Expected compiler source at '{compilerPath}'.");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var expressionOpText = File.ReadAllText(expressionOpPath);
        var compilerText = File.ReadAllText(compilerPath);
        var contractText = File.ReadAllText(contractPath);
        var declaredOpKinds = ExtractEnumMemberNames(expressionOpText, "ExpressionOpKind");
        var compilerReferencedOpKinds = ExtractExpressionOpKindReferences(compilerText);
        var unreferencedOpKinds = declaredOpKinds.Except(compilerReferencedOpKinds, StringComparer.Ordinal);

        AssertSameSet(
            UnifiedBytecodeExpressionOpCompilerGapNames,
            unreferencedOpKinds,
            "Expression op kinds without unified compiler references");
        foreach (var gapName in UnifiedBytecodeExpressionOpCompilerGapNames)
        {
            Assert.Contains($"`{gapName}`", contractText, StringComparison.Ordinal);
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

    private static string[] ExtractExecutionInstructionRecordNames(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\binternal\s+sealed\s+record\s+(?<name>[A-Za-z0-9_]+Instruction)\b[\s\S]*?:\s*ExecutionInstruction\b",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string ExtractSourceSection(string sourceText, string startMarker, string endMarker)
    {
        var startIndex = sourceText.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Source text is missing start marker '{startMarker}'.");
        var endIndex = sourceText.IndexOf(endMarker, startIndex + startMarker.Length, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Source text is missing end marker '{endMarker}'.");
        return sourceText.Substring(startIndex, endIndex - startIndex);
    }

    private static string[] ExtractExecutionInstructionCases(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\bcase\s+(?<name>[A-Za-z0-9_]+Instruction)\b",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string[] ExtractEnumMemberNames(string sourceText, string enumName)
    {
        var enumMatch = Regex.Match(
            sourceText,
            $@"\benum\s+{Regex.Escape(enumName)}\s*(?::\s*\w+)?\s*\{{(?<body>.*?)^\}}",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(enumMatch.Success, $"Source text is missing enum '{enumName}'.");

        return enumMatch.Groups["body"].Value
            .Split('\n')
            .Select(line => line.Split("//", StringSplitOptions.None)[0].Trim().TrimEnd(','))
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static string[] ExtractExpressionOpKindReferences(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\bExpressionOpKind\.(?<name>[A-Za-z0-9_]+)\b",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string[] ExtractUnifiedBytecodeOpcodeCases(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\bcase\s+UnifiedBytecodeOpCode\.(?<name>[A-Za-z0-9_]+)\s*:",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string[] ExtractBacktickedBulletItemsUnderHeading(string documentText, string heading)
    {
        var headingIndex = documentText.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(headingIndex >= 0, $"Contract document is missing heading '{heading}'.");

        var sectionStart = documentText.IndexOf('\n', headingIndex);
        Assert.True(sectionStart >= 0, $"Contract document heading '{heading}' has no content.");
        sectionStart++;

        var nextHeadingMatch = Regex.Match(
            documentText[sectionStart..],
            "^#{2,3} ",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var sectionLength = nextHeadingMatch.Success ? nextHeadingMatch.Index : documentText.Length - sectionStart;
        var sectionText = documentText.Substring(sectionStart, sectionLength);

        return Regex.Matches(
                sectionText,
                "^- `(?<name>[A-Za-z0-9_:-]+)`",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static void AssertSameSet(IEnumerable<string> expected, IEnumerable<string> actual, string subject)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);
        var missing = expectedSet
            .Except(actualSet, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var unexpected = actualSet
            .Except(expectedSet, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0 && unexpected.Length == 0,
            $"{subject} mismatch. Missing: {FormatList(missing)}. Unexpected: {FormatList(unexpected)}.");
    }

    private static string FormatList(IReadOnlyCollection<string> items)
    {
        return items.Count == 0 ? "<none>" : string.Join(", ", items);
    }
}

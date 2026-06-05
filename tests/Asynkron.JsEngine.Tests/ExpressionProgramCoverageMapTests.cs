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
        "pre-gate:IsClassConstructor",
        "pre-gate:IsArrowFunction",
        "pre-gate:IsAsyncLike",
        "pre-gate:IsGenerator",
        "pre-gate:hasParameterExpressions",
        "pre-gate:hasOnlySimpleIdentifierParameters",
        "pre-gate:usesArguments",
        "pre-gate:needsArgumentsBinding",
        "pre-gate:allowIdentifierCache",
        "pre-gate:lexicalThisEnvironment",
        "pre-gate:superConstructor",
        "pre-gate:superPrototype",
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
    ];

    private static readonly string[] UnifiedBytecodeExpressionOpCompilerGapNames =
    [
    ];

    private static readonly string[] UnifiedBytecodeExpressionOpGeneralLoopGapNames =
    [
    ];

    private static readonly string[] UnifiedBytecodeSyncPrototypeGuardGapNames =
    [
        "AwaitAndDiscard",
        "AwaitValue",
        "AwaitedReturn",
        "StoreResumeValue",
        "Yield",
        "YieldStar"
    ];

    private static readonly string[] UnifiedBytecodeResumableOpcodeAllowListGapNames =
    [
        "ApplyBindingTarget",
        "DeclareClass",
        "DeclareDynamicLexical",
        "DeclareDynamicVar",
        "DeclareFunction",
        "EnterCatch",
        "EnterWith",
        "GetComputedPropertyForCompoundSet",
        "GetNamedPropertyForCompoundSet",
        "InitializeDynamicLexical",
        "LeaveWith",
        "PopEnvironment",
        "PushEnvironment",
        "StoreDynamicIdentifier",
        "SuperConstructInvocationBoundary",
        "ThrowReferenceError"
    ];

    private static readonly string[] UnifiedBytecodeResumableInstructionAllowListGapNames =
    [
        "BreakInstruction",
        "ClassDeclarationInstruction",
        "ContinueInstruction",
        "EnterCatchInstruction",
        "EnterWithInstruction",
        "LeaveWithInstruction",
        "PopEnvironmentInstruction",
        "PushEnvironmentInstruction",
        "SetCompletionValueInstruction"
    ];

    private static readonly string[] A35ObjectLiteralMemberLeafNames =
    [
        "A35a:DefineComputedObjectProperty",
        "A35b:DefineObjectMethod",
        "A35c:DefineComputedObjectMethod",
        "A35d:DefineObjectAccessor",
        "A35e:DefineComputedObjectAccessor"
    ];

    private static readonly string[] B24ClassExpressionLeafNames =
    [
        "B24a:ClassExpressionConstructor",
        "B24b:ClassExpressionInstanceFields",
        "B24c:ClassExpressionStaticFields",
        "B24d:ClassExpressionStaticBlocks",
        "B24e:ClassExpressionPrivateFields",
        "B24f:ClassExpressionPrivateMethods",
        "B24g:ClassExpressionAccessors",
        "B24h:ClassExpressionComputedMembers",
        "B24i:ClassExpressionSuperInMembers"
    ];

    private static readonly string[] StaleDiscardDeclinePhrases =
    [
        "non-directive discarded expressions still decline before VM execution",
        "Keep every non-directive discarded expression declined before VM execution"
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
            "NonLiteralDefaultParameter_DoesNotUseUnifiedBytecodeProductionFastPath",
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
        Assert.Contains("### Sync Prototype Opcode Guard Gaps (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("### Resumable Opcode Allowlist Gaps (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("### Resumable Instruction Allowlist Gaps (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("### A35 Object Literal Member Leaves (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("### B24 Class Expression Leaves (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("### Compiler Decline Owner Leaves (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("### Compiler Decline Reason Templates (current)", contractText, StringComparison.Ordinal);
        Assert.Contains("## Reserved Ownership Lanes (planned, not implemented)", contractText, StringComparison.Ordinal);
        Assert.Contains("## Proof Commands", contractText, StringComparison.Ordinal);

        foreach (var opcodeName in Enum.GetNames<UnifiedBytecodeOpCode>())
        {
            Assert.Contains($"`{opcodeName}`", contractText, StringComparison.Ordinal);
        }

        var documentedOpcodeNames = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### Unified Opcode Inventory (current)");
        AssertSameSet(
            Enum.GetNames<UnifiedBytecodeOpCode>(),
            documentedOpcodeNames,
            "Unified opcode inventory");

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

        var checkedLedgerText = ExtractSourceSection(
            contractText,
            "## Checked Production Decline Ledger",
            "### Statement Diagnostics Supported Kinds (current)");
        var ledgerKeys = ContractLedgerRowPattern
            .Matches(checkedLedgerText)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var expectedLedgerKeys = declineCodeNames
            .Concat(ProductionPreGateLedgerKeys)
            .Concat(ProductionPrototypeGuardLedgerKeys);
        AssertSameSet(expectedLedgerKeys, ledgerKeys, "Production decline ledger keys");
    }

    [Fact]
    public void UnifiedBytecodeCompiler_DeclineReasonTemplatesMatchExpansionContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var compilerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeCompiler.cs");
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(compilerPath), $"Expected compiler source at '{compilerPath}'.");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var compilerText = File.ReadAllText(compilerPath);
        var contractText = File.ReadAllText(contractPath);
        var compilerReasonTemplates = ExtractCompilerReasonTemplates(compilerText);
        var documentedReasonTemplates = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### Compiler Decline Reason Templates (current)",
            allowSingleQuotes: true);

        Assert.NotEmpty(compilerReasonTemplates);
        AssertSameSet(
            compilerReasonTemplates,
            documentedReasonTemplates,
            "Unified bytecode compiler decline reason template inventory");
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
    public void UnifiedBytecodeCompiler_GeneralExpressionLoopDeclinesOnlyDocumentedGaps()
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
        var generalLoopText = ExtractSourceSection(
            compilerText,
            "private static bool TryAppendExpressionProgramOps(",
            "private static bool TryAppendFirstBoundaryCallTargetPreparation(");
        var generalLoopCases = ExtractExpressionOpKindCases(generalLoopText);
        var generalLoopGaps = declaredOpKinds.Except(generalLoopCases, StringComparer.Ordinal);
        var documentedGeneralLoopGaps = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### General Expression Lowering Gaps (current)");

        AssertSameSet(
            UnifiedBytecodeExpressionOpGeneralLoopGapNames,
            generalLoopGaps,
            "Expression op kinds without direct general unified expression-loop cases");
        AssertSameSet(
            UnifiedBytecodeExpressionOpGeneralLoopGapNames,
            documentedGeneralLoopGaps,
            "Documented general expression lowering gaps");
    }

    [Fact]
    public void UnifiedBytecodeExpansionContract_ListsCoarseLeafDecomposition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var contractText = File.ReadAllText(contractPath);
        var documentedA35Leaves = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### A35 Object Literal Member Leaves (current)");
        var documentedB24Leaves = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### B24 Class Expression Leaves (current)");

        AssertSameSet(
            A35ObjectLiteralMemberLeafNames,
            documentedA35Leaves,
            "A35 object literal member leaf decomposition");
        AssertSameSet(
            B24ClassExpressionLeafNames,
            documentedB24Leaves,
            "B24 class expression leaf decomposition");
    }

    [Fact]
    public void UnifiedBytecodeProductionEligibility_SyncPrototypeGuardDocumentsEveryNonAdmittedOpcode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var eligibilityPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeProductionEligibility.cs");
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(eligibilityPath), $"Expected eligibility source at '{eligibilityPath}'.");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var eligibilityText = File.ReadAllText(eligibilityPath);
        var contractText = File.ReadAllText(contractPath);
        var prototypeGuardText = ExtractSourceSection(
            eligibilityText,
            "private static bool TryFindPrototypeOnlyOpcode(",
            "private static void TryGetUnsupportedBinaryDecline(");
        var admittedOpcodes = ExtractUnifiedBytecodeOpcodeCases(prototypeGuardText);
        var unadmittedOpcodes = Enum.GetNames<UnifiedBytecodeOpCode>()
            .Except(admittedOpcodes, StringComparer.Ordinal);
        var documentedGaps = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### Sync Prototype Opcode Guard Gaps (current)");

        AssertSameSet(
            UnifiedBytecodeSyncPrototypeGuardGapNames,
            unadmittedOpcodes,
            "Sync prototype opcode guard gap inventory");
        AssertSameSet(
            UnifiedBytecodeSyncPrototypeGuardGapNames,
            documentedGaps,
            "Documented sync prototype opcode guard gaps");
    }

    [Fact]
    public void UnifiedBytecodeDiscardDocumentation_DoesNotPreserveStaleBlanketDeclineRule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkedPaths = new[]
        {
            Path.Combine(repositoryRoot.FullName, "docs", "rules", "unified-bytecode-prototypes.md"),
            Path.Combine(repositoryRoot.FullName, "docs", "adrs", "0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md"),
            Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md")
        };

        foreach (var checkedPath in checkedPaths)
        {
            Assert.True(File.Exists(checkedPath), $"Expected discard documentation at '{checkedPath}'.");
            var text = File.ReadAllText(checkedPath);
            foreach (var stalePhrase in StaleDiscardDeclinePhrases)
            {
                Assert.DoesNotContain(stalePhrase, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void UnifiedBytecodeResumableEligibility_AllowsEveryResumableVmOpcode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var eligibilityPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeProductionEligibility.cs");
        var virtualMachinePath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeVirtualMachine.cs");
        Assert.True(File.Exists(eligibilityPath), $"Expected eligibility source at '{eligibilityPath}'.");
        Assert.True(File.Exists(virtualMachinePath), $"Expected VM source at '{virtualMachinePath}'.");

        var eligibilityText = File.ReadAllText(eligibilityPath);
        var virtualMachineText = File.ReadAllText(virtualMachinePath);
        var resumableAllowListText = ExtractSourceSection(
            eligibilityText,
            "private static bool TryFindUnsupportedResumableOpcode(",
            "private static bool TryFindInstructionDynamicIdentifierDecline(");
        var executeResumableText = ExtractSourceSection(
            virtualMachineText,
            "public static UnifiedBytecodeStepResult ExecuteResumable(",
            "private static bool TryConsumePendingAwaitResume(");
        var allowListOpcodes = ExtractUnifiedBytecodeOpcodeReferences(resumableAllowListText);
        var resumableVmOpcodes = ExtractUnifiedBytecodeOpcodeCases(executeResumableText);

        AssertSameSet(resumableVmOpcodes, allowListOpcodes, "Resumable unified bytecode opcode allow-list");
    }

    [Fact]
    public void UnifiedBytecodeResumableEligibility_DocumentsEveryNonAllowlistedOpcode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var eligibilityPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeProductionEligibility.cs");
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(eligibilityPath), $"Expected eligibility source at '{eligibilityPath}'.");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var eligibilityText = File.ReadAllText(eligibilityPath);
        var contractText = File.ReadAllText(contractPath);
        var resumableAllowListText = ExtractSourceSection(
            eligibilityText,
            "private static bool TryFindUnsupportedResumableOpcode(",
            "private static bool TryFindInstructionDynamicIdentifierDecline(");
        var allowListOpcodes = ExtractUnifiedBytecodeOpcodeReferences(resumableAllowListText);
        var unallowlistedOpcodes = Enum.GetNames<UnifiedBytecodeOpCode>()
            .Except(allowListOpcodes, StringComparer.Ordinal);
        var documentedGaps = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### Resumable Opcode Allowlist Gaps (current)");

        AssertSameSet(
            UnifiedBytecodeResumableOpcodeAllowListGapNames,
            unallowlistedOpcodes,
            "Resumable unified bytecode opcode allow-list gap inventory");
        AssertSameSet(
            UnifiedBytecodeResumableOpcodeAllowListGapNames,
            documentedGaps,
            "Documented resumable opcode allow-list gaps");
    }

    [Fact]
    public void UnifiedBytecodeResumableEligibility_DocumentsEveryNonAllowlistedInstruction()
    {
        var repositoryRoot = FindRepositoryRoot();
        var instructionsPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "Instructions",
            "Instructions.cs");
        var eligibilityPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeProductionEligibility.cs");
        var contractPath = Path.Combine(repositoryRoot.FullName, "docs", "unified-bytecode-expansion-contract.md");
        Assert.True(File.Exists(instructionsPath), $"Expected instruction source at '{instructionsPath}'.");
        Assert.True(File.Exists(eligibilityPath), $"Expected eligibility source at '{eligibilityPath}'.");
        Assert.True(File.Exists(contractPath), $"Expected contract doc at '{contractPath}'.");

        var instructionsText = File.ReadAllText(instructionsPath);
        var eligibilityText = File.ReadAllText(eligibilityPath);
        var contractText = File.ReadAllText(contractPath);
        var declaredInstructions = ExtractExecutionInstructionRecordNames(instructionsText);
        var resumableInstructionAllowListText = ExtractSourceSection(
            eligibilityText,
            "private static bool IsSupportedResumableInstruction(",
            "private static bool TryGetResumableExpressionProgram(");
        var allowListInstructions = ExtractExecutionInstructionCases(resumableInstructionAllowListText);
        var unallowlistedInstructions = declaredInstructions.Except(
            allowListInstructions,
            StringComparer.Ordinal);
        var documentedGaps = ExtractBacktickedBulletItemsUnderHeading(
            contractText,
            "### Resumable Instruction Allowlist Gaps (current)");

        AssertSameSet(
            UnifiedBytecodeResumableInstructionAllowListGapNames,
            unallowlistedInstructions,
            "Resumable unified bytecode instruction allow-list gap inventory");
        AssertSameSet(
            UnifiedBytecodeResumableInstructionAllowListGapNames,
            documentedGaps,
            "Documented resumable instruction allow-list gaps");
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

    private static string[] ExtractExpressionOpKindCases(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\bcase\s+ExpressionOpKind\.(?<name>[A-Za-z0-9_]+)\b",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string[] ExtractUnifiedBytecodeOpcodeReferences(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\bUnifiedBytecodeOpCode\.(?<name>[A-Za-z0-9_]+)\b",
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

    private static string[] ExtractCompilerReasonTemplates(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\breason\s*=\s*(?<expression>.*?);",
                RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["expression"].Value)
            .Where(expression => !expression.Contains("string.Empty", StringComparison.Ordinal) ||
                                 expression.Contains('?', StringComparison.Ordinal))
            .SelectMany(ExtractStringLiteralTemplates)
            .Where(template => template.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(template => template, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> ExtractStringLiteralTemplates(string expressionText)
    {
        return Regex.Matches(
                expressionText,
                @"\$?""(?<text>(?:\\.|[^""\\])*)""",
                RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(match => Regex.Replace(
                match.Groups["text"].Value,
                @"\s+",
                " ",
                RegexOptions.CultureInvariant).Trim());
    }

    private static string[] ExtractBacktickedBulletItemsUnderHeading(
        string documentText,
        string heading,
        bool allowSingleQuotes = false)
    {
        var headingMatch = Regex.Match(
            documentText,
            $"^{Regex.Escape(heading)}\\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(headingMatch.Success, $"Contract document is missing heading '{heading}'.");

        var sectionStart = documentText.IndexOf('\n', headingMatch.Index + headingMatch.Length);
        Assert.True(sectionStart >= 0, $"Contract document heading '{heading}' has no content.");
        sectionStart++;

        var nextHeadingMatch = Regex.Match(
            documentText[sectionStart..],
            "^#{2,3} ",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var sectionLength = nextHeadingMatch.Success ? nextHeadingMatch.Index : documentText.Length - sectionStart;
        var sectionText = documentText.Substring(sectionStart, sectionLength);

        var characterClass = allowSingleQuotes ? @"[A-Za-z0-9_:\- '.{},?()/\[\]+*<>=""\\|;#&!]+?" : @"[A-Za-z0-9_:-]+";
        return Regex.Matches(
                sectionText,
                $"^- `(?<name>{characterClass})`",
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

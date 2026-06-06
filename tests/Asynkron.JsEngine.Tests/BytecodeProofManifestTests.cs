using System.Text.Json;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed partial class BytecodeProofManifestTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ManifestRelativePath = "docs/plans/bytecode-proof-manifest.json";
    private const string ChecklistRelativePath = "docs/plans/bytecode-burndown-checklist.md";

    public static TheoryData<string> ProofIds
    {
        get
        {
            var rows = new TheoryData<string>();
            foreach (var proof in LoadManifest().Items.SelectMany(static item => item.Proofs))
            {
                rows.Add(proof.Id);
            }

            return rows;
        }
    }

    [Fact]
    public void Manifest_ItemsMatchChecklistStatus()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checklistText = File.ReadAllText(Path.Combine(repositoryRoot.FullName, ChecklistRelativePath));
        var checkedRows = ExtractChecklistStatuses(checklistText);

        foreach (var item in LoadManifest().Items)
        {
            Assert.True(
                checkedRows.TryGetValue(item.Id, out var isChecked),
                $"{ManifestRelativePath}: item '{item.Id}' is not present in {ChecklistRelativePath}.");

            var expectsChecked = string.Equals(item.Status, "done", StringComparison.Ordinal);
            Assert.Equal(expectsChecked, isChecked);
        }
    }

    [Fact]
    public void Manifest_StatusDoesNotHideOpenProofRows()
    {
        foreach (var item in LoadManifest().Items)
        {
            Assert.NotEmpty(item.Proofs);

            var hasOpenProof = item.Proofs.Any(static proof =>
                string.Equals(proof.Claim, "open", StringComparison.Ordinal) ||
                string.Equals(proof.Claim, "hard-quarantined", StringComparison.Ordinal));

            if (string.Equals(item.Status, "done", StringComparison.Ordinal))
            {
                Assert.False(
                    hasOpenProof,
                    $"{item.Id}: done items cannot contain open or hard-quarantined proof rows.");
            }
            else
            {
                Assert.True(
                    hasOpenProof,
                    $"{item.Id}: non-done items must name the executable open/quarantined boundary.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProofIds))]
    public async Task ProofRows_Hold(string proofId)
    {
        var manifest = LoadManifest();
        var item = manifest.Items.Single(item => item.Proofs.Any(proof => proof.Id == proofId));
        var proof = item.Proofs.Single(proof => proof.Id == proofId);

        switch (proof.Kind)
        {
            case "eligibility":
                VerifyEligibility(item, proof);
                break;
            case "runtime":
                await VerifyRuntime(proof);
                break;
            case "source-presence":
                VerifySourcePresence(proof, shouldExist: true);
                break;
            case "source-absence":
                VerifySourcePresence(proof, shouldExist: false);
                break;
            case "standalone-expression-compile":
                VerifyStandaloneExpressionCompile(proof);
                break;
            case "general-expression-loop-coverage":
                VerifyGeneralExpressionLoopCoverage(proof);
                break;
            default:
                throw new InvalidOperationException($"{proof.Id}: unsupported proof kind '{proof.Kind}'.");
        }
    }

    private static void VerifyEligibility(ProofManifestItem item, ProofManifestProof proof)
    {
        var result = EvaluateEligibility(proof);
        Assert.Equal(proof.ExpectedEligible, result.IsEligible);

        if (proof.ExpectedEligible)
        {
            Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
            return;
        }

        Assert.False(
            string.Equals(item.Status, "done", StringComparison.Ordinal),
            $"{item.Id}: a done item cannot rely on a decline proof row.");

        if (!string.IsNullOrWhiteSpace(proof.DeclineCode))
        {
            Assert.True(
                Enum.TryParse<UnifiedBytecodeProductionDeclineCode>(proof.DeclineCode, out var expectedCode),
                $"{proof.Id}: unknown decline code '{proof.DeclineCode}'.");
            Assert.Equal(expectedCode, result.Code);
        }

        if (!string.IsNullOrWhiteSpace(proof.ReasonContains))
        {
            Assert.Contains(proof.ReasonContains, result.Reason, StringComparison.Ordinal);
        }
    }

    private async Task VerifyRuntime(ProofManifestProof proof)
    {
        await using var engine = CreateEngine();

        if (!string.IsNullOrWhiteSpace(proof.ExpectedThrowContains))
        {
            var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
                await engine.Evaluate(Require(proof.Source, proof.Id, nameof(proof.Source))));
            Assert.Contains(proof.ExpectedThrowContains, exception.Message, StringComparison.Ordinal);
        }
        else
        {
            var result = await engine.Evaluate(Require(proof.Source, proof.Id, nameof(proof.Source)));
            if (!string.IsNullOrWhiteSpace(proof.ExpectedResult))
            {
                Assert.Equal(proof.ExpectedResult, result?.ToString());
            }
        }

        var snapshot = CurrentLogger!.Collector.Snapshot();
        foreach (var requiredLog in proof.RequiredLogs)
        {
            Assert.Contains(
                snapshot,
                record => record.Message.Contains(requiredLog, StringComparison.Ordinal));
        }

        foreach (var forbiddenLog in proof.ForbiddenLogs)
        {
            Assert.DoesNotContain(
                snapshot,
                record => record.Message.Contains(forbiddenLog, StringComparison.Ordinal));
        }
    }

    private static void VerifySourcePresence(ProofManifestProof proof, bool shouldExist)
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot.FullName, Require(proof.Path, proof.Id, nameof(proof.Path)));
        Assert.True(File.Exists(path), $"{proof.Id}: expected source file '{path}'.");

        var source = File.ReadAllText(path);
        var contains = source.Contains(Require(proof.Pattern, proof.Id, nameof(proof.Pattern)), StringComparison.Ordinal);
        Assert.Equal(shouldExist, contains);
    }

    private static UnifiedBytecodeProductionEligibilityResult EvaluateEligibility(ProofManifestProof proof)
    {
        var subject = Require(proof.Subject, proof.Id, nameof(proof.Subject));
        return subject switch
        {
            "function" => UnifiedBytecodeProductionEligibility.Evaluate(
                GetFunctionPlan(proof),
                new UnifiedBytecodeProductionActivationDescriptor()),
            "script" => UnifiedBytecodeProductionEligibility.EvaluateScript(GetScriptPlan(proof)),
            "resumable-generator" => UnifiedBytecodeProductionEligibility.EvaluateResumable(
                GetFunctionPlan(proof),
                new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true)),
            "resumable-async" => UnifiedBytecodeProductionEligibility.EvaluateResumable(
                GetFunctionPlan(proof),
                new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true)),
            "resumable-async-generator" => UnifiedBytecodeProductionEligibility.EvaluateResumable(
                GetFunctionPlan(proof),
                new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true)),
            _ => throw new InvalidOperationException($"{proof.Id}: unsupported subject '{subject}'.")
        };
    }

    private static void VerifyStandaloneExpressionCompile(ProofManifestProof proof)
    {
        var plan = GetFunctionPlan(proof);
        var expressionInstruction = Assert.Single(plan.Instructions.OfType<EvaluateAndDiscardInstruction>());
        Assert.True(
            UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram(
                expressionInstruction.ExpressionProgram,
                allowsDynamicIdentifiers: true,
                out var program,
                out var reason),
            reason);

        if (!string.IsNullOrWhiteSpace(proof.RequiredOpCode))
        {
            Assert.True(
                Enum.TryParse<UnifiedBytecodeOpCode>(proof.RequiredOpCode, out var requiredOpCode),
                $"{proof.Id}: unknown opcode '{proof.RequiredOpCode}'.");
            Assert.Contains(program.Instructions, instruction => instruction.OpCode == requiredOpCode);
        }

        if (proof.RequiresBindingTargetConstants)
        {
            Assert.NotEmpty(program.BindingTargetConstants);
        }
    }

    private static void VerifyGeneralExpressionLoopCoverage(ProofManifestProof proof)
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
        Assert.True(File.Exists(expressionOpPath), $"{proof.Id}: expected expression op source at '{expressionOpPath}'.");
        Assert.True(File.Exists(compilerPath), $"{proof.Id}: expected compiler source at '{compilerPath}'.");

        var expressionOpText = File.ReadAllText(expressionOpPath);
        var compilerText = File.ReadAllText(compilerPath);
        var declaredOpKinds = ExtractEnumMemberNames(expressionOpText, "ExpressionOpKind");
        var generalLoopText = ExtractSourceSection(
            compilerText,
            "private static bool TryAppendExpressionProgramOps(",
            "private static bool TryAppendFirstBoundaryCallTargetPreparation(");
        var generalLoopCases = ExtractExpressionOpKindCases(generalLoopText);
        var missingCases = declaredOpKinds
            .Except(generalLoopCases, StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingCases);
    }

    private static ExecutionPlan GetFunctionPlan(ProofManifestProof proof)
    {
        var functionName = Require(proof.FunctionName, proof.Id, nameof(proof.FunctionName));
        var pipeline = AstTestHelpers.ParseAndAnalyze(Require(proof.Source, proof.Id, nameof(proof.Source)));
        var declaration = Assert.IsType<FunctionDeclaration>(
            pipeline.Analyzed.Body.Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static ExecutionPlan GetScriptPlan(ProofManifestProof proof)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(Require(proof.Source, proof.Id, nameof(proof.Source)));
        var cache = ((IAstCacheable<ScriptPlanCache>)pipeline.Analyzed).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static Dictionary<string, bool> ExtractChecklistStatuses(string checklistText)
    {
        var statuses = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Match match in ChecklistRowPattern().Matches(checklistText))
        {
            statuses[match.Groups["id"].Value] = string.Equals(match.Groups["mark"].Value, "x", StringComparison.Ordinal);
        }

        return statuses;
    }

    private static ProofManifest LoadManifest()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(repositoryRoot.FullName, ManifestRelativePath);
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProofManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(manifest);
        Assert.Equal(1, manifest.Version);
        Assert.NotEmpty(manifest.Items);
        return manifest;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ManifestRelativePath)))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string Require(string? value, string proofId, string propertyName)
    {
        Assert.False(string.IsNullOrWhiteSpace(value), $"{proofId}: missing required property '{propertyName}'.");
        return value!;
    }

    private static string ExtractSourceSection(string sourceText, string startMarker, string endMarker)
    {
        var start = sourceText.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Source is missing start marker '{startMarker}'.");
        var end = sourceText.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Source is missing end marker '{endMarker}'.");
        return sourceText[start..end];
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
            .Where(static line => line.Length > 0)
            .ToArray();
    }

    private static string[] ExtractExpressionOpKindCases(string sourceText)
    {
        return Regex.Matches(
                sourceText,
                @"\bcase\s+ExpressionOpKind\.(?<name>[A-Za-z0-9_]+)\b",
                RegexOptions.CultureInvariant)
            .Select(static match => match.Groups["name"].Value)
            .ToArray();
    }

    [GeneratedRegex(@"^- \[(?<mark>x| )\] \*\*(?<id>[A-Z][0-9]+[a-z]?(?:[0-9]+)?)\*\*", RegexOptions.Multiline)]
    private static partial Regex ChecklistRowPattern();

    private sealed class ProofManifest
    {
        public int Version { get; set; }

        public List<ProofManifestItem> Items { get; set; } = [];
    }

    private sealed class ProofManifestItem
    {
        public string Id { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public List<ProofManifestProof> Proofs { get; set; } = [];
    }

    private sealed class ProofManifestProof
    {
        public string Id { get; set; } = string.Empty;

        public string Claim { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string? Subject { get; set; }

        public string? FunctionName { get; set; }

        public string? Source { get; set; }

        public bool ExpectedEligible { get; set; }

        public string? DeclineCode { get; set; }

        public string? ReasonContains { get; set; }

        public string? ExpectedResult { get; set; }

        public string? ExpectedThrowContains { get; set; }

        public List<string> RequiredLogs { get; set; } = [];

        public List<string> ForbiddenLogs { get; set; } = [];

        public string? Path { get; set; }

        public string? Pattern { get; set; }

        public string? RequiredOpCode { get; set; }

        public bool RequiresBindingTargetConstants { get; set; }
    }
}

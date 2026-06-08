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

    [Fact]
    public void Manifest_StaticBlockFallback_IsClassifiedDeclinedResidue()
    {
        var proof = LoadManifest()
            .Items
            .SelectMany(static item => item.Proofs)
            .Single(static proof => proof.Id == "E5-static-block-ir-fallback-still-runs-execution-plan-runner");

        Assert.Equal("E5-static-block-declined-residue", proof.ChildOwner);
        Assert.Contains("explicit declined static-block residue", proof.Classification, StringComparison.Ordinal);
        Assert.Contains("not ordinary E5c script fallback retirement", proof.Classification, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_ClassConstructorFallback_IsSplitFromOrdinarySyncFunctionFallback()
    {
        var proofs = LoadManifest()
            .Items
            .SelectMany(static item => item.Proofs)
            .ToDictionary(static proof => proof.Id, StringComparer.Ordinal);

        var ordinaryProof = proofs["E5-function-runner-fallback-still-constructs-runner"];
        var classConstructorProof = proofs["E5-class-constructor-runner-fallback-still-constructs-runner"];

        Assert.Equal(
            "CreateClassifiedOrdinarySyncFunctionFallbackRunner",
            ordinaryProof.Pattern);
        Assert.Contains("not class-constructor initialization residue", ordinaryProof.Classification, StringComparison.Ordinal);

        Assert.Equal("E5d-class-constructor-initialization-residue", classConstructorProof.ChildOwner);
        Assert.Equal(
            "CreateClassifiedClassConstructorFallbackRunner",
            classConstructorProof.Pattern);
        Assert.Contains("explicit class-constructor fallback runner construction residue", classConstructorProof.Classification, StringComparison.Ordinal);
        Assert.Contains("not ordinary sync function fallback retirement", classConstructorProof.Classification, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_SyncGeneratorDeclinedBodyRunner_IsResidueSpecific()
    {
        var proof = LoadManifest()
            .Items
            .SelectMany(static item => item.Proofs)
            .Single(static proof => proof.Id == "E5-sync-generator-declined-residue-runner-still-present");

        Assert.Equal("source-presence", proof.Kind);
        Assert.Equal("open", proof.Claim);
        Assert.Equal("E5d-sync-generator-declined-residue", proof.ChildOwner);
        Assert.Equal("CreateClassifiedSyncGeneratorDeclinedResidueRunner", proof.Pattern);
        Assert.Contains("explicit sync generator declined-residue runner bridge", proof.Classification, StringComparison.Ordinal);
        Assert.Contains("pre-gate ordinary declines fail explicitly", proof.Classification, StringComparison.Ordinal);
        Assert.Contains("no generic declined-body runner remains", proof.Classification, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_ExecutionPlanRunnerEntryAnchors_AreClassifiedAllowlistsOrRetired()
    {
        var proofs = LoadManifest()
            .Items
            .SelectMany(static item => item.Proofs)
            .Where(static proof => proof.Id.StartsWith("E5-ir-runner-", StringComparison.Ordinal))
            .ToDictionary(static proof => proof.Id, StringComparer.Ordinal);

        Assert.Equal(
            [
                "E5-ir-runner-async-step-entry-still-present",
                "E5-ir-runner-script-entry-still-present",
                "E5-ir-runner-sync-entry-still-present",
                "E5-ir-runner-type-still-present"
            ],
            proofs.Keys.Order(StringComparer.Ordinal).ToArray());

        var asyncStepProof = proofs["E5-ir-runner-async-step-entry-still-present"];
        Assert.Equal("source-presence", asyncStepProof.Kind);
        Assert.Equal("open", asyncStepProof.Claim);
        Assert.Equal("E5d-async-function-declined-body-runner-residue", asyncStepProof.ChildOwner);
        Assert.Equal(".ExecuteAsyncStep(", asyncStepProof.Pattern);

        foreach (var proof in proofs.Values.Where(static proof => proof.Kind == "source-allowlist"))
        {
            Assert.Equal("source-allowlist", proof.Kind);
            Assert.Equal("open", proof.Claim);
            Assert.NotEmpty(proof.Paths);
            Assert.NotEmpty(proof.AllowedPaths);
            Assert.Contains("not a broad E5b", proof.Classification, StringComparison.Ordinal);
        }

        Assert.Equal("E5c-script-and-static-block-runner-fallback", proofs["E5-ir-runner-script-entry-still-present"].ChildOwner);
        Assert.Equal("E5d-function-and-constructor-runner-fallback", proofs["E5-ir-runner-sync-entry-still-present"].ChildOwner);
    }

    [Fact]
    public void Manifest_FinalE5Retrospective_KeepsBatchOpenUntilResidueOwnersRetire()
    {
        var items = LoadManifest().Items.ToDictionary(static item => item.Id, StringComparer.Ordinal);

        Assert.Equal("done", items["E5a"].Status);
        Assert.Equal("open", items["E5b"].Status);
        Assert.Equal("open", items["E5c"].Status);
        Assert.Equal("open", items["E5d"].Status);
        Assert.Equal("open", items["E5e"].Status);

        Assert.All(
            items["E5b"].Proofs.Where(static proof => proof.Claim == "open" && proof.Kind == "source-allowlist"),
            proof =>
            {
                Assert.Equal("source-allowlist", proof.Kind);
                Assert.Equal("open", proof.Claim);
            });
        Assert.Single(
            items["E5b"].Proofs,
            static proof =>
                proof.Id == "E5-ir-runner-async-step-entry-still-present" &&
                proof.Kind == "source-presence" &&
                proof.Claim == "open");

        Assert.Equal(
            [
                "E5-static-block-declined-residue",
                "E5c-script-fallback-retirement"
            ],
            items["E5c"].Proofs.Select(static proof => proof.ChildOwner).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            [
                "E5d-async-function-declined-body-runner-residue",
                "E5d-class-constructor-initialization-residue",
                "E5d-function-and-resumable-declined-body-runner-retirement",
                "E5d-sync-generator-declined-residue"
            ],
            items["E5d"].Proofs.Select(static proof => proof.ChildOwner).Order(StringComparer.Ordinal).ToArray());
        Assert.Single(items["E5e"].Proofs, static proof => proof.Claim == "hard-quarantined");
    }

    [Fact]
    public void Manifest_E5eProof_IsExactTerminalDynamicResidueExclusion()
    {
        var proof = LoadManifest()
            .Items
            .Single(static item => item.Id == "E5e")
            .Proofs
            .Single(static proof => proof.Id == "E5-terminal-dynamic-residue-excluded-from-ordinary-runner-retirement");

        Assert.Equal("E5e-residue-exclusion-boundary", proof.ChildOwner);
        Assert.Equal("hard-quarantined", proof.Claim);
        Assert.Equal("source-presence", proof.Kind);
        Assert.Contains("A2/D1/D2/D3/D4-style quarantine rows", proof.Classification, StringComparison.Ordinal);
        Assert.Contains("not ordinary E5 runner retirement", proof.Classification, StringComparison.Ordinal);
        Assert.DoesNotContain("E5b", proof.Classification, StringComparison.Ordinal);
        Assert.DoesNotContain("E5c", proof.Classification, StringComparison.Ordinal);
        Assert.DoesNotContain("E5d", proof.Classification, StringComparison.Ordinal);

        Assert.Equal(
            [
                "Terminal dynamic residue remains excluded from ordinary runner retirement",
                "direct eval with runtime-source, multi-arg, spread, or declaration-injecting",
                "awaited-with object evaluation",
                "retained live `with` scopes outside the VM current-environment lane",
                "eval-injected runtime bindings",
                "`Function(...)`-produced bodies",
                "A2/D1/D2/D3/D4-style quarantine boundaries outside ordinary E5 runner retirement"
            ],
            proof.Patterns);
    }

    [Fact]
    public void Manifest_B36OpenRowsKeepDynamicEvalHelpersSeparateFromClassDeclarationResidue()
    {
        var b36 = LoadManifest().Items.Single(static item => item.Id == "B36");
        var openProofIds = b36.Proofs
            .Where(static proof => string.Equals(proof.Claim, "open", StringComparison.Ordinal))
            .Select(static proof => proof.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal("open", b36.Status);
        Assert.Equal(
            [
                "B36-class-declaration-computed-field-activation-capture-stays-open",
                "B36-class-declaration-computed-member-activation-capture-stays-open",
                "B36-class-declaration-non-production-static-block-stays-open",
                "B36-class-declaration-private-instance-field-declines",
                "B36-class-declaration-private-instance-method-captures-activation-declines",
                "B36-class-declaration-private-instance-method-computed-neighbor-declines",
                "B36-class-declaration-private-static-method-declines",
                "B36-class-declaration-static-block-ir-fallback-stays-open",
                "B36-class-declaration-static-block-runtime-source-direct-eval-declines",
                "B36-class-declaration-static-field-shape-guard-stays-open",
                "B36-class-declaration-static-member-shape-guard-stays-open",
                "B36-deferred-class-definition-environment-bridge-stays-open",
                "B36-resumable-arguments-eval-helper-decline-stays-open",
                "B36-resumable-dynamic-eval-helper-activation-decline-stays-open",
                "B36-resumable-helper-direct-eval-cache-decline-anchor-stays-open",
                "B36-resumable-helper-synthetic-activation-capture-decline-anchor-stays-open"
            ],
            openProofIds);

        var repositoryRoot = FindRepositoryRoot();
        var checklistText = File.ReadAllText(Path.Combine(repositoryRoot.FullName, ChecklistRelativePath));
        Assert.All(
            openProofIds,
            proofId => Assert.Contains($"`{proofId}`", checklistText, StringComparison.Ordinal));

        var helperProofs = b36.Proofs
            .Where(static proof => proof.Id.StartsWith("B36-resumable-", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, helperProofs.Length);
        Assert.All(helperProofs, static proof =>
        {
            Assert.Equal("source-presence", proof.Kind);
            Assert.Equal("src/Asynkron.JsEngine/Ast/TypedAstEvaluator.UnifiedBytecodeResumableActivation.cs", proof.Path);
        });

        var runtimeSourceStaticBlockProof = b36.Proofs.Single(static proof =>
            proof.Id == "B36-class-declaration-static-block-runtime-source-direct-eval-declines");
        Assert.Equal("eligibility", runtimeSourceStaticBlockProof.Kind);
        Assert.Equal("open", runtimeSourceStaticBlockProof.Claim);
        Assert.Equal("resumable-generator", runtimeSourceStaticBlockProof.Subject);
        Assert.Equal("UnsupportedPlanShape", runtimeSourceStaticBlockProof.DeclineCode);
        Assert.Contains(
            "Direct eval invocation semantics",
            runtimeSourceStaticBlockProof.ReasonContains,
            StringComparison.Ordinal);
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
            case "source-allowlist":
                VerifySourceAllowlist(proof);
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
            var source = Require(proof.Source, proof.Id, nameof(proof.Source));
            var result = proof.AwaitResult
                ? await engine.EvaluateAndAwait(source)
                : await engine.Evaluate(source);
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
        var patterns = proof.Patterns.Count > 0
            ? proof.Patterns
            : [Require(proof.Pattern, proof.Id, nameof(proof.Pattern))];
        var scannedFiles = !string.IsNullOrWhiteSpace(proof.Path)
            ? [Path.Combine(repositoryRoot.FullName, proof.Path)]
            : proof.Paths
                .SelectMany(path => EnumerateManifestSourceFiles(repositoryRoot, proof.Id, path))
                .ToArray();

        Assert.NotEmpty(scannedFiles);

        foreach (var path in scannedFiles)
        {
            Assert.True(File.Exists(path), $"{proof.Id}: expected source file '{path}'.");

            var source = File.ReadAllText(path);
            foreach (var pattern in patterns)
            {
                var contains = source.Contains(pattern, StringComparison.Ordinal);
                Assert.Equal(shouldExist, contains);
            }
        }
    }

    private static void VerifySourceAllowlist(ProofManifestProof proof)
    {
        var repositoryRoot = FindRepositoryRoot();
        Assert.NotEmpty(proof.Paths);
        Assert.NotEmpty(proof.AllowedPaths);
        var pattern = Require(proof.Pattern, proof.Id, nameof(proof.Pattern));
        var scannedFiles = proof.Paths
            .SelectMany(path => EnumerateManifestSourceFiles(repositoryRoot, proof.Id, path))
            .ToArray();

        Assert.NotEmpty(scannedFiles);
        foreach (var allowedPath in proof.AllowedPaths)
        {
            Assert.Contains(scannedFiles, file => NormalizeManifestPath(repositoryRoot, file) == allowedPath);
        }

        var allowedPaths = proof.AllowedPaths.ToHashSet(StringComparer.Ordinal);
        var matches = scannedFiles
            .SelectMany(file =>
            {
                var relativePath = NormalizeManifestPath(repositoryRoot, file);
                return File.ReadAllLines(file)
                    .Select((line, index) => new { line, index })
                    .Where(entry => entry.line.Contains(pattern, StringComparison.Ordinal))
                    .Select(entry => (relativePath, LineNumber: entry.index + 1, Text: entry.line.Trim()));
            })
            .ToArray();
        Assert.NotEmpty(matches);

        var disallowed = matches
            .Where(match => !allowedPaths.Contains(match.relativePath))
            .Select(match => $"{match.relativePath}:{match.LineNumber}:{match.Text}")
            .ToArray();

        Assert.True(
            disallowed.Length == 0,
            $"{proof.Id}: source allowlist drift detected for '{pattern}':\n" + string.Join('\n', disallowed));

        VerifyClassifiedCallSites(proof, matches);
    }

    private static void VerifyClassifiedCallSites(
        ProofManifestProof proof,
        (string relativePath, int LineNumber, string Text)[] matches)
    {
        if (proof.ClassifiedCallSites.Count == 0)
        {
            return;
        }

        foreach (var callSite in proof.ClassifiedCallSites)
        {
            Assert.True(
                proof.AllowedPaths.Contains(callSite.Path, StringComparer.Ordinal),
                $"{proof.Id}: classified call site '{callSite.Path}' must be listed in allowedPaths.");

            Assert.False(
                string.IsNullOrWhiteSpace(callSite.ChildOwner),
                $"{proof.Id}: classified call site '{callSite.Path}' is missing childOwner.");
            Assert.False(
                string.IsNullOrWhiteSpace(callSite.Classification),
                $"{proof.Id}: classified call site '{callSite.Path}' is missing classification.");
        }

        var countRows = proof.ClassifiedCallSites
            .Where(static callSite => callSite.CallCount.HasValue)
            .ToArray();
        if (countRows.Length != 0)
        {
            Assert.Equal(
                countRows
                    .GroupBy(static callSite => callSite.Path, StringComparer.Ordinal)
                    .OrderBy(static group => group.Key, StringComparer.Ordinal)
                    .Select(static group => (Path: group.Key, Count: group.Sum(static callSite => callSite.CallCount!.Value)))
                    .ToArray(),
                matches
                    .GroupBy(static match => match.relativePath, StringComparer.Ordinal)
                    .OrderBy(static group => group.Key, StringComparer.Ordinal)
                    .Select(static group => (Path: group.Key, Count: group.Count()))
                    .ToArray());
        }

        var memberRows = proof.ClassifiedCallSites
            .Where(static callSite =>
                !string.IsNullOrWhiteSpace(callSite.EnclosingMember) ||
                !string.IsNullOrWhiteSpace(callSite.DynamicLabel))
            .ToArray();
        if (memberRows.Length == 0)
        {
            return;
        }

        var expectedMembersByPath = memberRows
            .Where(static callSite => !string.IsNullOrWhiteSpace(callSite.EnclosingMember))
            .GroupBy(static callSite => callSite.Path, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static callSite => callSite.EnclosingMember!).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var expectedCallSites = memberRows
            .Select(static callSite => new ClassifiedSourceCallSite(
                callSite.Path,
                callSite.EnclosingMember ?? string.Empty,
                callSite.DynamicLabel ?? string.Empty,
                callSite.ChildOwner,
                callSite.Classification))
            .OrderBy(static callSite => callSite.RelativePath)
            .ThenBy(static callSite => callSite.EnclosingMember)
            .ThenBy(static callSite => callSite.DynamicLabel)
            .ToArray();
        var expectedPaths = expectedCallSites
            .Select(static callSite => callSite.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        var repositoryRoot = FindRepositoryRoot();
        var actualCallSites = matches
            .Where(match => expectedPaths.Contains(match.relativePath))
            .Select(match =>
            {
                var path = Path.Combine(repositoryRoot.FullName, match.relativePath);
                var lines = File.ReadAllLines(path);
                var lineIndex = match.LineNumber - 1;
                var member = expectedMembersByPath.TryGetValue(match.relativePath, out var members)
                    ? FindEnclosingMemberName(lines, lineIndex, members)
                    : string.Empty;
                var dynamicLabel = memberRows.Any(callSite => string.Equals(callSite.Path, match.relativePath, StringComparison.Ordinal) &&
                                                              !string.IsNullOrWhiteSpace(callSite.DynamicLabel))
                    ? FindDynamicExecutorLabel(lines, lineIndex)
                    : string.Empty;

                var expected = memberRows.SingleOrDefault(callSite =>
                    string.Equals(callSite.Path, match.relativePath, StringComparison.Ordinal) &&
                    string.Equals(callSite.EnclosingMember ?? string.Empty, member, StringComparison.Ordinal) &&
                    string.Equals(callSite.DynamicLabel ?? string.Empty, dynamicLabel, StringComparison.Ordinal));

                return new ClassifiedSourceCallSite(
                    match.relativePath,
                    member,
                    dynamicLabel,
                    expected?.ChildOwner ?? "unclassified child owner",
                    expected?.Classification ?? "unclassified source call site");
            })
            .OrderBy(static callSite => callSite.RelativePath)
            .ThenBy(static callSite => callSite.EnclosingMember)
            .ThenBy(static callSite => callSite.DynamicLabel)
            .ToArray();

        Assert.Equal(expectedCallSites, actualCallSites);
    }

    private static IEnumerable<string> EnumerateManifestSourceFiles(
        DirectoryInfo repositoryRoot,
        string proofId,
        string relativePath)
    {
        var path = Path.Combine(repositoryRoot.FullName, relativePath);
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        Assert.True(Directory.Exists(path), $"{proofId}: expected source path '{path}'.");
        foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static string NormalizeManifestPath(DirectoryInfo repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot.FullName, path).Replace('\\', '/');

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

    private static string FindEnclosingMemberName(string[] lines, int callLineIndex, string[] expectedMemberNames)
    {
        for (var i = callLineIndex; i >= 0; i--)
        {
            var line = lines[i].Trim();
            foreach (var expectedMemberName in expectedMemberNames)
            {
                if (Regex.IsMatch(
                        line,
                        $@"(?<![A-Za-z0-9_.]){Regex.Escape(expectedMemberName)}(?:<[^>]+>)?\(",
                        RegexOptions.CultureInvariant))
                {
                    return expectedMemberName;
                }
            }
        }

        return "<unknown>";
    }

    private static string FindDynamicExecutorLabel(string[] lines, int callLineIndex)
    {
        for (var i = callLineIndex; i < Math.Min(lines.Length, callLineIndex + 8); i++)
        {
            var line = lines[i];
            var labelStart = line.IndexOf("\"Dynamic ", StringComparison.Ordinal);
            if (labelStart < 0)
            {
                continue;
            }

            var labelEnd = line.IndexOf('"', labelStart + 1);
            if (labelEnd > labelStart)
            {
                return line.Substring(labelStart + 1, labelEnd - labelStart - 1);
            }
        }

        return "<unknown>";
    }

    private sealed record ClassifiedSourceCallSite(
        string RelativePath,
        string EnclosingMember,
        string DynamicLabel,
        string ChildOwner,
        string Classification);

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

        public string ChildOwner { get; set; } = string.Empty;

        public string Classification { get; set; } = string.Empty;

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

        public bool AwaitResult { get; set; }

        public string? Path { get; set; }

        public string? Pattern { get; set; }

        public List<string> Patterns { get; set; } = [];

        public List<string> Paths { get; set; } = [];

        public List<string> AllowedPaths { get; set; } = [];

        public List<ProofManifestClassifiedCallSite> ClassifiedCallSites { get; set; } = [];

        public string? RequiredOpCode { get; set; }

        public bool RequiresBindingTargetConstants { get; set; }
    }

    private sealed class ProofManifestClassifiedCallSite
    {
        public string ChildOwner { get; set; } = string.Empty;

        public string Classification { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public int? CallCount { get; set; }

        public string? EnclosingMember { get; set; }

        public string? DynamicLabel { get; set; }
    }
}

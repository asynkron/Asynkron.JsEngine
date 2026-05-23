using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class ExecutionPlanDiagnosticsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private static readonly Regex AstExpressionEvaluatorPattern = new(@"\b(?:EvaluateLegacyAstExpression|EvaluateLegacyAstExpressionSlow|ProfileEvaluateExpression)\s*\(", RegexOptions.Compiled);

    [Fact]
    public void FunctionPlanCache_Reads_DoNotInflateBuildCounters()
    {
        ExecutionPlanDiagnostics.Reset();

        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            function add(a, b) {
                return a + b;
            }
            """);
        var add = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body[0]).Function;

        var first = ((IAstCacheable<ExecutionPlanCache>)add).GetOrCreateCache();
        var second = ((IAstCacheable<ExecutionPlanCache>)add).GetOrCreateCache();

        Assert.True(first.Succeeded, first.FailureReason);
        Assert.Same(first.Plan, second.Plan);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(1, snapshot.Functions.Attempts);
        Assert.Equal(1, snapshot.Functions.Succeeded);
        Assert.Equal(0, snapshot.Functions.Failed);
        Assert.Equal(1, snapshot.FunctionCacheHits);
    }

    [Fact]
    public async Task DetailedSnapshot_TracksScriptBuilds_Separately()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let a = 1;
            let b = 2;
            a + b;
            """);

        Assert.Equal(3d, result);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(0, snapshot.Functions.Attempts);
        Assert.Equal(1, snapshot.Scripts.Attempts);
        Assert.Equal(1, snapshot.Scripts.Succeeded);
        Assert.Equal(0, snapshot.Scripts.Failed);
        Assert.Empty(snapshot.FailureCodes);
    }

    [Fact]
    public async Task DetailedSnapshot_BucketsUnsupportedBuilds_ByFailureCode()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function broken() {
                break;
            }
            """);
        var broken = Assert.IsType<FunctionDeclaration>(program.Body[0]).Function;

        var result = ExecutionPlanBuilder.Build(broken);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(1, snapshot.Functions.Attempts);
        Assert.Equal(0, snapshot.Scripts.Attempts);
        Assert.Equal(0, snapshot.Scripts.Succeeded);
        Assert.Equal(0, snapshot.Scripts.Failed);
        Assert.Equal(0, snapshot.Functions.Succeeded);
        Assert.Equal(1, snapshot.Functions.Failed);
        Assert.True(snapshot.FailureCodes.TryGetValue(result.Failure!.Code, out var count));
        Assert.Equal(1, count);
        Assert.Equal(result.Failure.Code, ExecutionPlanDiagnostics.LastFailureCode);
        Assert.Equal("broken", ExecutionPlanDiagnostics.LastFunctionDescription);
    }

    [Fact]
    public async Task DetailedSnapshot_BucketsUnsupportedScriptExpressionPrograms_ByExpressionFailureCode()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("tag`value`;");
        var result = ExecutionPlanBuildResult.FailureResult(
            ExecutionPlanFailureCode.UnsupportedExpressionProgram,
            "Expression bytecode does not yet support optional tagged templates.",
            ExpressionProgramFailureCode.OptionalTaggedTemplate);

        ExecutionPlanDiagnostics.ReportScriptResult(program, result);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(0, snapshot.Functions.Attempts);
        Assert.Equal(1, snapshot.Scripts.Attempts);
        Assert.Equal(0, snapshot.Scripts.Succeeded);
        Assert.Equal(1, snapshot.Scripts.Failed);
        Assert.True(snapshot.FailureCodes.TryGetValue(ExecutionPlanFailureCode.UnsupportedExpressionProgram, out var failureCount));
        Assert.Equal(1, failureCount);
        Assert.True(snapshot.ExpressionFailureCodes.TryGetValue(ExpressionProgramFailureCode.OptionalTaggedTemplate, out var expressionCount));
        Assert.Equal(1, expressionCount);
        Assert.Equal(ExpressionProgramFailureCode.OptionalTaggedTemplate, ExecutionPlanDiagnostics.LastExpressionFailureCode);
    }

    [Fact]
    public void ExpressionProgramFailureClassification_CoversCurrentBacklogBuckets()
    {
        var expression = new IdentifierExpression(null, Symbol.Intern("value"));
        var representativeDetails = new (string Detail, ExpressionProgramFailureCode ExpectedCode)[]
        {
            ("Expression bytecode does not yet support delete on super or optional member expressions.", ExpressionProgramFailureCode.UnsupportedDeleteTarget),
            ("Expression bytecode does not yet support super call expressions.", ExpressionProgramFailureCode.SuperCall),
            ("Expression bytecode does not yet support nested optional call expressions.", ExpressionProgramFailureCode.NestedOptionalCall),
            ("Expression bytecode does not yet support super or optional member update expressions.", ExpressionProgramFailureCode.OptionalOrSuperMemberUpdate),
            ("Expression bytecode does not yet support super tagged templates.", ExpressionProgramFailureCode.SuperTaggedTemplate),
            ("Expression bytecode does not yet support optional tagged templates.", ExpressionProgramFailureCode.OptionalTaggedTemplate),
            ("Expression bytecode does not yet support nested optional tagged templates.", ExpressionProgramFailureCode.NestedOptionalTaggedTemplate),
            ("Expression bytecode does not yet support super or optional property assignments.", ExpressionProgramFailureCode.OptionalOrSuperPropertyAssignment),
            ("Expression bytecode does not yet support super or optional index assignments.", ExpressionProgramFailureCode.OptionalOrSuperIndexAssignment),
            ("Expression bytecode does not yet support super member access.", ExpressionProgramFailureCode.SuperMemberAccess),
            ("Expression bytecode only supports lowered binary compound assignments.", ExpressionProgramFailureCode.UnsupportedCompoundAssignmentShape),
            ("Expression bytecode only supports identifier and member update expressions.", ExpressionProgramFailureCode.UnsupportedUpdateTarget),
            ("Expression bytecode only supports static string object property names.", ExpressionProgramFailureCode.UnsupportedStaticObjectPropertyName),
            ("Expression bytecode only supports static literal object property names.", ExpressionProgramFailureCode.UnsupportedStaticObjectPropertyName),
            ("Computed object property names must use an expression key.", ExpressionProgramFailureCode.InvalidComputedObjectKey),
            ("Expression bytecode only supports literal property names for dot access.", ExpressionProgramFailureCode.UnsupportedDotAccessPropertyName),
            ("Expression bytecode only supports literal property names for direct member calls.", ExpressionProgramFailureCode.UnsupportedDirectMemberCallPropertyName),
            ("Expression bytecode only supports literal property names for tagged template member access.", ExpressionProgramFailureCode.UnsupportedTaggedTemplateMemberAccessName),
            ("Expression bytecode does not yet support optional or super member call targets.", ExpressionProgramFailureCode.OptionalOrSuperMemberCallTarget),
            ("Expression bytecode does not yet support object member kind 'Spread'.", ExpressionProgramFailureCode.UnsupportedObjectMemberKind),
            ("Expression bytecode does not yet support unary operator 'Delete'.", ExpressionProgramFailureCode.UnsupportedUnaryOperator),
            ("Expression bytecode does not yet support 'ImportExpression'.", ExpressionProgramFailureCode.UnsupportedExpressionNode)
        };

        var seenCodes = new HashSet<ExpressionProgramFailureCode>();
        foreach (var (detail, expectedCode) in representativeDetails)
        {
            var failure = ExpressionProgramCompiler.ClassifyFailure(expression, detail);
            Assert.Equal(expectedCode, failure.Code);
            seenCodes.Add(failure.Code);
        }

        var expectedCodes = Enum.GetValues<ExpressionProgramFailureCode>().ToHashSet();
        AssertEqualCodeSetsWithIntentMessage(
            expectedCodes,
            seenCodes,
            "Update this backlog probe only when bytecode classification categories intentionally change.");
    }

    [Fact]
    public void SourceGate_ExecutionPlanRunner_Partials_DoNotIntroduceAstExpressionEvaluationSeams()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runnerDirectory = Path.Combine(repositoryRoot.FullName, "src", "Asynkron.JsEngine", "Ast");
        var runnerFiles = Directory
            .EnumerateFiles(runnerDirectory, "TypedAstEvaluator.ExecutionPlanRunner*.cs", SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.True(
            runnerFiles.Length > 0,
            "Source gate invariant failed: no TypedAstEvaluator.ExecutionPlanRunner*.cs files were discovered.");

        var matches = runnerFiles
            .SelectMany(file =>
            {
                var relativePath = Path.GetRelativePath(repositoryRoot.FullName, file).Replace('\\', '/');
                return File.ReadAllLines(file)
                    .Select((line, index) => new { line, index })
                    .Where(entry => AstExpressionEvaluatorPattern.IsMatch(entry.line))
                    .Select(entry => $"{relativePath}:{entry.index + 1}:{entry.line.Trim()}");
            })
            .ToArray();

        Assert.True(
            matches.Length == 0,
            "ExecutionPlanRunner AST expression seams detected (raw evaluators only; EvaluateExpressionProgram is allowed):\n" + string.Join('\n', matches));
    }

    [Fact]
    public void SourceGate_DynamicExpressionProgramBridge_StaysInsideApprovedBoundarySurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot.FullName, "src", "Asynkron.JsEngine");
        var allowedCallSites = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/Asynkron.JsEngine/Ast/FunctionExpressionExtensions.cs",
            "src/Asynkron.JsEngine/Ast/VariableKindExtensions.cs",
            "src/Asynkron.JsEngine/Ast/Legacy/ExpressionNodeExtensions.cs",
            "src/Asynkron.JsEngine/Ast/Legacy/LoopPlanExtensions.cs",
            "src/Asynkron.JsEngine/Ast/Legacy/StatementNodeExtensions.cs",
            "src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExpressionPrograms.cs"
        };

        var matches = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
            {
                var relativePath = Path.GetRelativePath(repositoryRoot.FullName, file).Replace('\\', '/');
                return File.ReadAllLines(file)
                    .Select((line, index) => new { line, index })
                    .Where(entry => entry.line.Contains("EvaluateDynamicExpressionProgram(", StringComparison.Ordinal))
                    .Select(entry => (relativePath, entry.index + 1, entry.line.Trim()));
            })
            .ToArray();

        var disallowed = matches
            .Where(match => !allowedCallSites.Contains(match.relativePath))
            .Select(match => $"{match.relativePath}:{match.Item2}:{match.Item3}")
            .ToArray();

        Assert.True(
            disallowed.Length == 0,
            "EvaluateDynamicExpressionProgram call-site drift detected:\n" + string.Join('\n', disallowed));
    }

    [Fact]
    public void SourceGate_ExpressionOpKind_RuntimeAndDiagnosticSurfaces_DoNotDrift()
    {
        var repositoryRoot = FindRepositoryRoot();
        var values = Enum.GetValues<ExpressionOpKind>();
        var sourceTargets = new[]
        {
            new SourceGateTarget(
                "execution runner dispatch",
                Path.Combine("src", "Asynkron.JsEngine", "Ast", "TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs")),
            new SourceGateTarget(
                "stack-depth analysis",
                Path.Combine("src", "Asynkron.JsEngine", "Execution", "Instructions", "ExpressionOp.cs")),
            new SourceGateTarget(
                "execution-plan printer formatting",
                Path.Combine("src", "Asynkron.JsEngine", "Execution", "ExecutionPlanPrinter.cs"))
        };

        var missingByTarget = new List<string>();
        foreach (var target in sourceTargets)
        {
            var fullPath = Path.Combine(repositoryRoot.FullName, target.RelativePath);
            var sourceText = File.ReadAllText(fullPath);
            var missing = values
                .Where(kind => !ContainsExpressionOpKindToken(sourceText, kind))
                .OrderBy(kind => kind)
                .ToArray();
            if (missing.Length == 0)
            {
                continue;
            }

            var missingNames = string.Join(", ", missing.Select(kind => kind.ToString()));
            var normalizedPath = target.RelativePath.Replace('\\', '/');
            missingByTarget.Add($"{target.SurfaceName} ({normalizedPath}): {missingNames}");
        }

        Assert.True(
            missingByTarget.Count == 0,
            $$"""
            ExpressionOpKind drift gate failed.
            Missing enum coverage:
            {{string.Join(Environment.NewLine, missingByTarget)}}

            Keep this allowlist-free guard strict: when adding or renaming ExpressionOpKind values,
            update runner dispatch, stack-depth analysis, and printer formatting in the same change.
            """);
    }

    [Fact]
    public async Task DetailedSnapshot_UnsupportedExpressionProgramBuckets_MatchRepresentativeProbe()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var probes = new[]
        {
            (
                Name: "direct member call with non-literal property",
                Expression: new CallExpression(
                    null,
                    new MemberExpression(
                        null,
                        new IdentifierExpression(null, Symbol.Intern("box")),
                        new IdentifierExpression(null, Symbol.Intern("dynamicPropertyName")),
                        IsComputed: false,
                        IsOptional: true),
                    [],
                    IsOptional: false),
                ExpectedFailureCode: ExpressionProgramFailureCode.UnsupportedDirectMemberCallPropertyName)
        };

        var expectedBuckets = new Dictionary<ExpressionProgramFailureCode, int>();
        foreach (var probe in probes)
        {
            var program = engine.ParseProgram("""
                function probe(box) {
                    return box;
                }
                """);
            var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(program.Body));
            var function = declaration.Function;
            var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(function.Body.Statements));
            var mutatedReturnStatement = returnStatement with { Expression = probe.Expression };
            var mutatedBody = function.Body with { Statements = [mutatedReturnStatement] };
            var mutatedFunction = function with { Body = mutatedBody };
            var buildResult = ExecutionPlanBuilder.Build(mutatedFunction);

            Assert.False(buildResult.Succeeded, $"{probe.Name} should fail plan build.");
            Assert.NotNull(buildResult.Failure);
            Assert.Equal(ExecutionPlanFailureCode.UnsupportedExpressionProgram, buildResult.Failure!.Code);
            Assert.Equal(probe.ExpectedFailureCode, buildResult.Failure.ExpressionFailureCode);
            expectedBuckets[probe.ExpectedFailureCode] = expectedBuckets.TryGetValue(probe.ExpectedFailureCode, out var count) ? count + 1 : 1;
        }

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.True(snapshot.FailureCodes.TryGetValue(ExecutionPlanFailureCode.UnsupportedExpressionProgram, out var unsupportedExpressionBuilds));
        Assert.Equal(probes.Length, unsupportedExpressionBuilds);
        AssertEqualBucketsWithIntentMessage(
            expectedBuckets,
            snapshot.ExpressionFailureCodes,
            "Update expected unsupported-expression buckets only when migration intentionally changes bytecode support; this probe intentionally exercises real failing plan builds.");
    }

    [Fact]
    public async Task ScriptSmokeProbe_CommonStrictScriptPoisoners_DoNotFailPlanBuild()
    {
        await using var engine = CreateEngine();

        var cases = new (string Name, string Source)[]
        {
            ("function expression", "const fn = function(value) { return value + 1; }; fn(41);"),
            ("class expression", "const Box = class { value() { return 42; } }; new Box().value();"),
            ("object method", "const obj = { value() { return 42; } }; obj.value();"),
            ("object accessor", "const obj = { get value() { return 42; }, set value(next) { this._value = next; } }; obj.value;"),
            ("immutable assignment", "const value = 1; value = 2;")
        };

        foreach (var testCase in cases)
        {
            var program = engine.ParseProgram(testCase.Source);
            try
            {
                await engine.Evaluate(program);
            }
            catch
            {
                // The smoke probe cares about plan build, not runtime completion.
            }

            var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
            Assert.True(cache.Succeeded, $"{testCase.Name} should build an IR script plan. Failure: {cache.FailureReason}");
        }
    }

    [Fact]
    public async Task SyncSmokeProbe_HistoricallyUnsupportedOptionalAndSuperExpressionSurfaces_DoNotFailPlanBuild()
    {
        await using var engine = CreateEngine();

        var cases = new (string Name, string Source, Action<ProgramNode> AssertBuilds)[]
        {
            (
                "optional-chain expression pack",
                """
                function tag(strings) {
                    return strings[0];
                }

                function maybeDeleteNamed(box) {
                    return delete box?.value;
                }

                function maybeDeleteComputed(box, key) {
                    return delete box?.[key];
                }

                function maybeTag(box) {
                    return box?.tag`!`;
                }

                function maybeNestedTag(box) {
                    return box?.inner.tag`!`;
                }
                """,
                program =>
                {
                    AssertScriptPlanBuilds(program, "optional-chain expression pack");
                    AssertFunctionPlanBuilds(program, "maybeDeleteNamed");
                    AssertFunctionPlanBuilds(program, "maybeDeleteComputed");
                    AssertFunctionPlanBuilds(program, "maybeTag");
                    AssertFunctionPlanBuilds(program, "maybeNestedTag");
                }),
            (
                "super expression pack",
                """
                class Base {
                    get value() {
                        return this._value ?? 1;
                    }

                    set value(next) {
                        this._value = next;
                    }

                    method() {
                        return 40;
                    }
                }

                class Derived extends Base {
                    read() {
                        return super.value + 1;
                    }

                    call() {
                        return super.method() + 2;
                    }

                    write(next) {
                        return super.value = next;
                    }

                    bump(key) {
                        return super[key]++;
                    }
                }
                """,
                program =>
                {
                    AssertScriptPlanBuilds(program, "super expression pack");
                    AssertClassDefinitionBuilds(program, "Base");
                    AssertClassDefinitionBuilds(program, "Derived");
                })
        };

        foreach (var testCase in cases)
        {
            var program = engine.ParseProgram(testCase.Source);
            try
            {
                await engine.Evaluate(program);
            }
            catch
            {
                // The smoke probe cares about plan build, not runtime completion.
            }

            testCase.AssertBuilds(program);
        }
    }

    [Fact]
    public async Task SyncSmokeProbe_ClassDefinitionComputedNameAndInitializerSurfaces_DoNotFailPlanBuild()
    {
        await using var engine = CreateEngine();

        var cases = new (string Name, string Source, Action<ProgramNode> AssertBuilds)[]
        {
            (
                "computed class element name pack",
                """
                let i = 0;
                var empty = Object.create(null);

                class Box {
                    [i++] = i++;
                    static [i++] = i++;
                    [i++] = i++;

                    get ['x' in empty]() {
                        return 'via get';
                    }

                    set ['x' in empty](param) {
                        this._setter = param;
                    }
                }
                """,
                program =>
                {
                    AssertScriptPlanBuilds(program, "computed class element name pack");
                    AssertClassDefinitionBuilds(program, "Box");
                }),
            (
                "class field initializer pack",
                """
                const helper = value => value + 1;
                const factory = () => value => value + 1;

                class Base {
                    static get value() {
                        return 41;
                    }

                    get value() {
                        return 41;
                    }
                }

                class Box extends Base {
                    field = helper?.(41);
                    nested = factory?.()(41);
                    viaSuper = super.value + 1;
                    methodValue = { answer() { return 42; } }.answer();
                    static total = super.value + 1;
                }
                """,
                program =>
                {
                    AssertScriptPlanBuilds(program, "class field initializer pack");
                    AssertClassDefinitionBuilds(program, "Base");
                    AssertClassDefinitionBuilds(program, "Box");
                })
        };

        foreach (var testCase in cases)
        {
            var program = engine.ParseProgram(testCase.Source);
            try
            {
                await engine.Evaluate(program);
            }
            catch
            {
                // The smoke probe cares about plan build, not runtime completion.
            }

            testCase.AssertBuilds(program);
        }
    }

    [Fact]
    public void DynamicWithAndGeneratorSeams_StayOnExpectedInstructionShapes()
    {
        var withEvalProgram = AstTestHelpers.ParseAndAnalyze("""
            function withEval(scope) {
                var local = 1;
                with (scope) {
                    eval("local = value + 1");
                }

                return local;
            }
            """);
        var withEval = AssertFunctionPlanBuilds(withEvalProgram.Analyzed, "withEval");
        var withInstruction = Assert.Single(withEval.Instructions.OfType<EnterWithInstruction>(), i => i.ObjectProgram is not null);
        Assert.NotNull(withInstruction.WithScopeSlot);
        AssertProgramContains<LoadIdentifierExpressionOp>(withInstruction.ObjectProgram, op => op.Name.Name == "scope");

        var evalStatement = Assert.Single(withEval.Instructions.OfType<EvaluateAndDiscardInstruction>());
        AssertProgramContains<CallExpressionOp>(
            evalStatement.ExpressionProgram,
            op => op.IsDirectEval && op.ArgumentCount == 1);

        var withYieldProgram = AstTestHelpers.ParseAndAnalyze("""
            function* withYield(scopeObj) {
                with (yield scopeObj) {
                    yield answer;
                }
            }
            """);
        var withYield = AssertFunctionPlanBuilds(withYieldProgram.Analyzed, "withYield");
        var yieldingWithInstruction = Assert.Single(withYield.Instructions.OfType<EnterWithInstruction>(), i => i.ObjectProgram is not null);
        AssertProgramContains<LoadIdentifierExpressionOp>(
            yieldingWithInstruction.ObjectProgram,
            op => op.Name.Name!.StartsWith("__yield_lower_resume", StringComparison.Ordinal));

        var relayProgram = AstTestHelpers.ParseAndAnalyze("""
            async function* relay(values) {
                yield* await values;
            }
            """);
        var relay = AssertFunctionPlanBuilds(relayProgram.Analyzed, "relay");
        var yieldStar = Assert.Single(relay.Instructions.OfType<YieldStarInstruction>(), i => i.AwaitedProgram is not null);
        Assert.Null(yieldStar.IterableProgram);
        AssertProgramContains<LoadIdentifierExpressionOp>(yieldStar.AwaitedProgram, op => op.Name.Name == "values");
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_RoundTrips_EncodeNow_ControlFlowFamilies_FromPlan()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            function sample(flag) {
                while (true) {
                    if (flag) {
                        break;
                    }

                    continue;
                }
            }
            """);
        var sample = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body[0]).Function;
        var cache = ((IAstCacheable<ExecutionPlanCache>)sample).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        Assert.NotNull(cache.Plan);

        var supportedInstructions = cache.Plan!.Instructions
            .Where(instruction => StatementInstructionDiagnosticsCodec.TryEncode(instruction, out _))
            .ToArray();

        Assert.NotEmpty(supportedInstructions);
        Assert.Contains(supportedInstructions, instruction => instruction is BreakInstruction);
        Assert.Contains(supportedInstructions, instruction => instruction is ContinueInstruction);
        Assert.Contains(supportedInstructions, instruction => instruction is BreakableExitInstruction);

        foreach (var instruction in supportedInstructions)
        {
            Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded));
            var decoded = StatementInstructionDiagnosticsCodec.Decode(encoded);
            AssertEquivalentInstruction(instruction, decoded);
        }
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_RoundTrips_SetCompletionValue_Family()
    {
        var instructions = new ExecutionInstruction[]
        {
            new SetCompletionValueInstruction(7),
            new JumpInstruction(9)
        };

        foreach (var instruction in instructions)
        {
            Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded));
            var decoded = StatementInstructionDiagnosticsCodec.Decode(encoded);
            AssertEquivalentInstruction(instruction, decoded);
        }
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_RoundTrips_ExpressionProgramBackedFamilies()
    {
        var targetSymbol = Symbol.Intern("slotTarget");
        var awaitState = Symbol.Intern("awaitState");
        var sharedProgram = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(123)));
        var instructions = new ExecutionInstruction[]
        {
            new EvaluateAndDiscardInstruction(1, sharedProgram, SuppressCompletionValue: true),
            new AwaitAndDiscardInstruction(2, awaitState, sharedProgram, SuppressCompletionValue: true),
            new ThrowInstruction(sharedProgram, awaitState, sharedProgram),
            new ReturnInstruction(3, sharedProgram, awaitState, sharedProgram),
            new AssignmentSlotInstruction(
                4,
                targetSymbol,
                SuppressCompletionValue: true,
                AllowNameInference: false,
                ScopeId: 13,
                SlotIndex: 7,
                FlatSlotId: 23),
            new SimpleVariableDeclarationInstruction(
                5,
                VariableKind.Const,
                targetSymbol,
                AllowNameInference: true,
                IsScriptLevel: true),
            new BindingVariableDeclarationInstruction(
                6,
                VariableKind.Let,
                new IdentifierBindingTargetProgram(targetSymbol),
                InitializerProgram: sharedProgram)
        };

        var expressionPrograms = new StatementDiagnosticsExpressionProgramTable();
        foreach (var instruction in instructions)
        {
            Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(instruction, expressionPrograms, out var encoded));
            var decoded = StatementInstructionDiagnosticsCodec.Decode(encoded, expressionPrograms);
            AssertEquivalentInstruction(instruction, decoded);
        }

        Assert.Equal(1, expressionPrograms.Count);
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_CompatibilityOverloads_PreserveExpressionPrograms()
    {
        var program = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.LoadLiteralConstant(0)),
            literalConstants: ImmutableArray.Create(JsValue.FromDouble(123)));
        var instruction = new ReturnInstruction(3, program, Symbol.Intern("awaitState"), program);

        Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded));
        var decoded = Assert.IsType<ReturnInstruction>(StatementInstructionDiagnosticsCodec.Decode(encoded));

        Assert.Equal(program, decoded.ReturnProgram);
        Assert.Equal(program, decoded.AwaitedProgram);
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_UsesTypedPayload_ForAssignmentScopeMetadata()
    {
        var instruction = new AssignmentSlotInstruction(
            Next: 4,
            TargetSymbol: Symbol.Intern("slotTarget"),
            SuppressCompletionValue: true,
            AllowNameInference: false,
            ScopeId: 13,
            SlotIndex: 7,
            FlatSlotId: 23);

        Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded));
        Assert.Equal(EncodedStatementOpcode.AssignmentSlot, encoded.Header.Opcode);
        Assert.Equal(13, encoded.Payload.ScopeId);
        Assert.Equal(23, encoded.Payload.FlatSlotId);
        Assert.Equal(7, encoded.Header.Extra);
        Assert.Equal(32, encoded.EstimatedCompactByteSize);

        var decoded = Assert.IsType<AssignmentSlotInstruction>(StatementInstructionDiagnosticsCodec.Decode(encoded));
        Assert.Equal(13, decoded.ScopeId);
        Assert.Equal(7, decoded.SlotIndex);
        Assert.Equal(23, decoded.FlatSlotId);
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_StoreResumeValue_RoundTripsWithAndWithoutTargetSymbol()
    {
        var withTarget = new StoreResumeValueInstruction(Next: 12, TargetSymbol: Symbol.Intern("resume_target"));
        Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(withTarget, out var withTargetEncoded));
        Assert.Equal(EncodedStatementOpcode.StoreResumeValue, withTargetEncoded.Header.Opcode);
        Assert.Equal(Symbol.Intern("resume_target"), withTargetEncoded.Payload.PrimarySymbol);
        var withTargetDecoded = Assert.IsType<StoreResumeValueInstruction>(StatementInstructionDiagnosticsCodec.Decode(withTargetEncoded));
        Assert.Equal(withTarget, withTargetDecoded);

        var withoutTarget = new StoreResumeValueInstruction(Next: 17, TargetSymbol: null);
        Assert.True(StatementInstructionDiagnosticsCodec.TryEncode(withoutTarget, out var withoutTargetEncoded));
        Assert.Equal(EncodedStatementOpcode.StoreResumeValue, withoutTargetEncoded.Header.Opcode);
        Assert.Null(withoutTargetEncoded.Payload.PrimarySymbol);
        var withoutTargetDecoded = Assert.IsType<StoreResumeValueInstruction>(StatementInstructionDiagnosticsCodec.Decode(withoutTargetEncoded));
        Assert.Equal(withoutTarget, withoutTargetDecoded);
    }

    [Fact]
    public void StatementInstructionDiagnosticCodec_InstructionKindClassification_IsExplicitAndDriftGated()
    {
        var expectedSupported = new HashSet<InstructionKind>
        {
            InstructionKind.Jump,
            InstructionKind.Break,
            InstructionKind.Continue,
            InstructionKind.SetCompletionValue,
            InstructionKind.BreakableExit,
            InstructionKind.EvaluateAndDiscard,
            InstructionKind.AwaitAndDiscard,
            InstructionKind.Throw,
            InstructionKind.Return,
            InstructionKind.AssignmentSlot,
            InstructionKind.SimpleVariableDeclaration,
            InstructionKind.BindingVariableDeclaration,
            InstructionKind.StoreResumeValue
        };

        var expectedUnsupported = new HashSet<InstructionKind>
        {
            InstructionKind.IncrementSlot,
            InstructionKind.LogicalCompoundAssignmentSlot,
            InstructionKind.FunctionDeclaration,
            InstructionKind.ClassDeclaration,
            InstructionKind.PushEnvironment,
            InstructionKind.PopEnvironment,
            InstructionKind.Yield,
            InstructionKind.YieldStar,
            InstructionKind.EnterTry,
            InstructionKind.EnterCatch,
            InstructionKind.LeaveTry,
            InstructionKind.BreakableEnter,
            InstructionKind.EndFinally,
            InstructionKind.IteratorInit,
            InstructionKind.IteratorMoveNext,
            InstructionKind.IteratorClose,
            InstructionKind.Branch,
            InstructionKind.EnterWith,
            InstructionKind.LeaveWith,
            InstructionKind.CompoundAssignmentSlot,
            InstructionKind.ForInInit,
            InstructionKind.ForInMoveNext,
            InstructionKind.ArrayDestructuringInit,
            InstructionKind.ArrayDestructuringElement,
            InstructionKind.ArrayDestructuringRest,
            InstructionKind.ArrayDestructuringClose
        };

        var allKinds = Enum.GetValues<InstructionKind>().ToHashSet();
        Assert.True(expectedSupported.IsSubsetOf(allKinds));
        Assert.True(expectedUnsupported.IsSubsetOf(allKinds));
        expectedSupported.UnionWith(expectedUnsupported);
        Assert.Equal(allKinds, expectedSupported);
        Assert.All(
            allKinds,
            kind => Assert.Equal(
                !expectedUnsupported.Contains(kind),
                StatementInstructionDiagnosticsCodec.IsSupportedKind(kind)));
    }

    [Fact]
    public void PrintCompactStatementSemanticView_ForPureControlFlowPlan_MatchesInstructionPrinterOutput()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new BreakInstruction(8, 2),
            new ContinueInstruction(10, 2)
        };
        var plan = new ExecutionPlan(instructions.ToImmutableArray(), EntryPoint: 1);

        var expected = ExecutionPlanPrinter.Print(plan.Instructions, plan.EntryPoint);
        var actual = ExecutionPlanDiagnostics.PrintCompactStatementSemanticView(plan);

        Assert.Equal(expected, actual);
    }

    private static void AssertScriptPlanBuilds(ProgramNode program, string description)
    {
        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"{description} should build an IR script plan. Failure: {cache.FailureReason}");
    }

    private static ExecutionPlan AssertFunctionPlanBuilds(ProgramNode program, string functionName)
    {
        var declaration = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration candidate &&
                                             candidate.Name.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Function '{functionName}' should build an IR plan. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static void AssertClassDefinitionBuilds(ProgramNode program, string className)
    {
        var declaration = Assert.IsType<ClassDeclaration>(
            program.Body.Single(statement => statement is ClassDeclaration candidate &&
                                             candidate.Name.Name == className));
        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)declaration.Definition).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Class '{className}' should build definition bytecode. Failure: {cache.FailureReason}");
        Assert.True(
            cache.Definition.Constructor.PlanSeed.Succeeded,
            $"Class '{className}' constructor should build an IR plan. Failure: {cache.Definition.Constructor.PlanSeed.FailureReason}");
        Assert.All(
            cache.Definition.Members,
            member => Assert.True(
                member.Callable.PlanSeed.Succeeded,
                $"Class '{className}' member '{member.Name}' should build an IR plan. Failure: {member.Callable.PlanSeed.FailureReason}"));
    }

    private static void AssertProgramContains<TOp>(ExpressionProgram? program, Func<ExpressionOpView, bool>? predicate = null)
        where TOp : IExpressionOpMarker
    {
        Assert.NotNull(program);
        Assert.Contains(
            program.Value.GetOps(TOp.Kind),
            op => predicate is null || predicate(op));
    }

    private static void AssertEqualBucketsWithIntentMessage(
        IReadOnlyDictionary<ExpressionProgramFailureCode, int> expected,
        IReadOnlyDictionary<ExpressionProgramFailureCode, int> actual,
        string updateGuidance)
    {
        if (expected.OrderBy(entry => entry.Key).SequenceEqual(actual.OrderBy(entry => entry.Key)))
        {
            return;
        }

        static string FormatBuckets(IReadOnlyDictionary<ExpressionProgramFailureCode, int> buckets)
        {
            if (buckets.Count == 0)
            {
                return "(none)";
            }

            var builder = new StringBuilder();
            foreach (var entry in buckets.OrderBy(entry => entry.Key))
            {
                builder.Append(entry.Key);
                builder.Append(": ");
                builder.Append(entry.Value);
                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        var message = $$"""
            Unsupported-expression bucket drift detected.
            Expected:
            {{FormatBuckets(expected)}}

            Actual:
            {{FormatBuckets(actual)}}

            {{updateGuidance}}
            """;
        Assert.Fail(message);
    }

    private static void AssertEqualCodeSetsWithIntentMessage(
        IReadOnlySet<ExpressionProgramFailureCode> expected,
        IReadOnlySet<ExpressionProgramFailureCode> actual,
        string updateGuidance)
    {
        if (expected.OrderBy(code => code).SequenceEqual(actual.OrderBy(code => code)))
        {
            return;
        }

        static string FormatSet(IEnumerable<ExpressionProgramFailureCode> values)
        {
            return string.Join(
                Environment.NewLine,
                values.OrderBy(value => value).Select(value => value.ToString()));
        }

        var message = $$"""
            Unsupported-expression classification bucket drift detected.
            Expected:
            {{FormatSet(expected)}}

            Actual:
            {{FormatSet(actual)}}

            {{updateGuidance}}
            """;
        Assert.Fail(message);
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

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private sealed record SourceGateTarget(string SurfaceName, string RelativePath);

    private static bool ContainsExpressionOpKindToken(string sourceText, ExpressionOpKind kind)
    {
        var pattern = $@"\bExpressionOpKind\.{Regex.Escape(kind.ToString())}\b";
        return Regex.IsMatch(sourceText, pattern, RegexOptions.CultureInvariant);
    }

    private static void AssertEquivalentInstruction(ExecutionInstruction expected, ExecutionInstruction actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());

        switch (expected)
        {
            case AssignmentSlotInstruction expectedAssignment:
                var actualAssignment = Assert.IsType<AssignmentSlotInstruction>(actual);
                Assert.Equal(expectedAssignment.Next, actualAssignment.Next);
                Assert.Equal(expectedAssignment.TargetSymbol, actualAssignment.TargetSymbol);
                Assert.Equal(expectedAssignment.ValueProgram, actualAssignment.ValueProgram);
                Assert.Equal(expectedAssignment.AwaitStateKey, actualAssignment.AwaitStateKey);
                Assert.Equal(expectedAssignment.AwaitedProgram, actualAssignment.AwaitedProgram);
                Assert.Equal(expectedAssignment.SuppressCompletionValue, actualAssignment.SuppressCompletionValue);
                Assert.Equal(expectedAssignment.AllowNameInference, actualAssignment.AllowNameInference);
                Assert.Equal(expectedAssignment.ScopeId, actualAssignment.ScopeId);
                Assert.Equal(expectedAssignment.SlotIndex, actualAssignment.SlotIndex);
                Assert.Equal(expectedAssignment.FlatSlotId, actualAssignment.FlatSlotId);
                return;

            case SimpleVariableDeclarationInstruction expectedDeclaration:
                var actualDeclaration = Assert.IsType<SimpleVariableDeclarationInstruction>(actual);
                Assert.Equal(expectedDeclaration.Next, actualDeclaration.Next);
                Assert.Equal(expectedDeclaration.VarKind, actualDeclaration.VarKind);
                Assert.Equal(expectedDeclaration.TargetSymbol, actualDeclaration.TargetSymbol);
                Assert.Equal(expectedDeclaration.InitializerProgram, actualDeclaration.InitializerProgram);
                Assert.Equal(expectedDeclaration.AwaitStateKey, actualDeclaration.AwaitStateKey);
                Assert.Equal(expectedDeclaration.AwaitedProgram, actualDeclaration.AwaitedProgram);
                Assert.Equal(expectedDeclaration.AllowNameInference, actualDeclaration.AllowNameInference);
                Assert.Equal(expectedDeclaration.IsScriptLevel, actualDeclaration.IsScriptLevel);
                return;

            case BindingVariableDeclarationInstruction expectedBindingDeclaration:
                var actualBindingDeclaration = Assert.IsType<BindingVariableDeclarationInstruction>(actual);
                Assert.Equal(expectedBindingDeclaration.Next, actualBindingDeclaration.Next);
                Assert.Equal(expectedBindingDeclaration.VarKind, actualBindingDeclaration.VarKind);
                Assert.Equal(expectedBindingDeclaration.TargetProgram, actualBindingDeclaration.TargetProgram);
                Assert.Equal(expectedBindingDeclaration.InitializerProgram, actualBindingDeclaration.InitializerProgram);
                Assert.Equal(expectedBindingDeclaration.AwaitStateKey, actualBindingDeclaration.AwaitStateKey);
                Assert.Equal(expectedBindingDeclaration.AwaitedProgram, actualBindingDeclaration.AwaitedProgram);
                return;

            case EvaluateAndDiscardInstruction expectedEvaluateAndDiscard:
                var actualEvaluateAndDiscard = Assert.IsType<EvaluateAndDiscardInstruction>(actual);
                Assert.Equal(expectedEvaluateAndDiscard.Next, actualEvaluateAndDiscard.Next);
                Assert.Equal(expectedEvaluateAndDiscard.ExpressionProgram, actualEvaluateAndDiscard.ExpressionProgram);
                Assert.Equal(expectedEvaluateAndDiscard.SuppressCompletionValue, actualEvaluateAndDiscard.SuppressCompletionValue);
                return;

            case AwaitAndDiscardInstruction expectedAwaitAndDiscard:
                var actualAwaitAndDiscard = Assert.IsType<AwaitAndDiscardInstruction>(actual);
                Assert.Equal(expectedAwaitAndDiscard.Next, actualAwaitAndDiscard.Next);
                Assert.Equal(expectedAwaitAndDiscard.AwaitStateKey, actualAwaitAndDiscard.AwaitStateKey);
                Assert.Equal(expectedAwaitAndDiscard.AwaitedProgram, actualAwaitAndDiscard.AwaitedProgram);
                Assert.Equal(expectedAwaitAndDiscard.SuppressCompletionValue, actualAwaitAndDiscard.SuppressCompletionValue);
                return;

            case ThrowInstruction expectedThrow:
                var actualThrow = Assert.IsType<ThrowInstruction>(actual);
                Assert.Equal(expectedThrow.ThrowProgram, actualThrow.ThrowProgram);
                Assert.Equal(expectedThrow.AwaitStateKey, actualThrow.AwaitStateKey);
                Assert.Equal(expectedThrow.AwaitedProgram, actualThrow.AwaitedProgram);
                return;

            case ReturnInstruction expectedReturn:
                var actualReturn = Assert.IsType<ReturnInstruction>(actual);
                Assert.Equal(expectedReturn.Next, actualReturn.Next);
                Assert.Equal(expectedReturn.ReturnProgram, actualReturn.ReturnProgram);
                Assert.Equal(expectedReturn.AwaitStateKey, actualReturn.AwaitStateKey);
                Assert.Equal(expectedReturn.AwaitedProgram, actualReturn.AwaitedProgram);
                return;
        }

        Assert.Equal(
            ExecutionPlanDiagnostics.FormatInstruction(expected),
            ExecutionPlanDiagnostics.FormatInstruction(actual));
    }
}

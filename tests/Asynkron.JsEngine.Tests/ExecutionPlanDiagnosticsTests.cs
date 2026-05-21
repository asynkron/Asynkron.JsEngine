using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class ExecutionPlanDiagnosticsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private static readonly Regex EvaluateExpressionPattern = new(@"EvaluateExpression\(", RegexOptions.Compiled);
    private static readonly Regex ProfileEvaluateExpressionPattern = new(@"ProfileEvaluateExpression\(", RegexOptions.Compiled);

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
                    .Where(entry => EvaluateExpressionPattern.IsMatch(entry.line) || ProfileEvaluateExpressionPattern.IsMatch(entry.line))
                    .Select(entry => $"{relativePath}:{entry.index + 1}:{entry.line.Trim()}");
            })
            .ToArray();

        Assert.True(
            matches.Length == 0,
            "ExecutionPlanRunner AST expression seams detected:\n" + string.Join('\n', matches));
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

    private static void AssertScriptPlanBuilds(ProgramNode program, string description)
    {
        var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"{description} should build an IR script plan. Failure: {cache.FailureReason}");
    }

    private static void AssertFunctionPlanBuilds(ProgramNode program, string functionName)
    {
        var declaration = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration candidate &&
                                             candidate.Name.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Function '{functionName}' should build an IR plan. Failure: {cache.FailureReason}");
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
}

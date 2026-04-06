using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class ExecutionPlanDiagnosticsTests(ITestOutputHelper output) : InternalTestBase(output)
{
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
}

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for B13: super-property reads inside the resumable VM. A generator/async class method
///     that reads <c>super.x</c> or <c>super[k]</c> between suspension points is admitted by
///     <see cref="UnifiedBytecodeProductionEligibility.EvaluateResumable" /> and resolves the live
///     home-object receiver/prototype through <see cref="UnifiedBytecodeResumeState.ResumableSuperBinding" />.
///     Neighboring super call/write/update families are covered by their own proof packs.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableSuperPropertyReadTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    [Fact]
    public void EvaluateResumable_GeneratorSuperReads_AdmitsSuperReadOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() { return 1; }
                get other() { return 2; }
            }

            class Derived extends Base {
                *read(name) {
                    yield 0;
                    yield super.value + super[name];
                }
            }
            """,
            "Derived",
            "read");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnsureSuperReference);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedSuperProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedSuperProperty);
    }

    [Fact]
    public void EvaluateResumable_AsyncSuperReads_AdmitsSuperReadOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() { return 3; }
                get other() { return 4; }
            }

            class Derived extends Base {
                async read(name, gate) {
                    await gate;
                    return super.value + super[name];
                }
            }
            """,
            "Derived",
            "read");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedSuperProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedSuperProperty);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorSuperReads_AdmitsSuperReadOpcodes()
    {
        var plan = GetClassMethodPlan("""
            class Base {
                get value() { return 5; }
                get other() { return 6; }
            }

            class Derived extends Base {
                async *read(name, gate) {
                    await gate;
                    yield super.value + super[name];
                }
            }
            """,
            "Derived",
            "read");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedSuperProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetComputedSuperProperty);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorSuperReadsAfterYield_RoutesResumableAndReadsBaseProperties()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() { return this.prefix + "A"; }
                get other() { return this.prefix + "B"; }
            }

            class Derived extends Base {
                constructor(prefix) {
                    super();
                    this.prefix = prefix;
                }

                *read(name) {
                    yield "gate";
                    yield super.value + "|" + super[name];
                }
            }

            var it = new Derived("d:").read("other");
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("gate|d:A|d:B", result);
        AssertGeneratorFastPath("<anonymous>", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncSuperReadsAfterAwait_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "pending";
            class Base {
                get value() { return this.prefix + "A"; }
                get other() { return this.prefix + "B"; }
            }

            class Derived extends Base {
                constructor(prefix) {
                    super();
                    this.prefix = prefix;
                }

                async read(name, gate) {
                    await gate;
                    return super.value + "|" + super[name];
                }
            }

            new Derived("a:").read("other", Promise.resolve(0))
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("a:A|a:B", result);
        AssertAsyncFastPath("<anonymous>", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorSuperReadsAfterAwait_RoutesResumableAndReadsBaseProperties()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = "pending";
            class Base {
                get value() { return this.prefix + "A"; }
                get other() { return this.prefix + "B"; }
            }

            class Derived extends Base {
                constructor(prefix) {
                    super();
                    this.prefix = prefix;
                }

                async *read(name, gate) {
                    await gate;
                    yield super.value + "|" + super[name];
                }
            }

            async function run() {
                var iterator = new Derived("ag:").read("other", Promise.resolve(0));
                var first = await iterator.next();
                var second = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    String(second.value) + ":" + second.done;
            }

            run().then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("ag:A|ag:B:false|undefined:true", result);
        AssertAsyncGeneratorFastPath("<anonymous>", argc: 2);
    }

    [Fact]
    public void SourceGate_ResumableSuperSetup_DoesNotCreateRunnerBackedEnvironment()
    {
        var repositoryRoot = FindRepositoryRoot();
        var invokerPaths = new[]
        {
            Path.Combine("src", "Asynkron.JsEngine", "Ast", "TypedAstEvaluator.SyncGeneratorInvoker.cs"),
            Path.Combine("src", "Asynkron.JsEngine", "Ast", "TypedAstEvaluator.AsyncFunctionInvoker.cs"),
            Path.Combine("src", "Asynkron.JsEngine", "Ast", "TypedAstEvaluator.AsyncGeneratorInvoker.cs")
        };

        Assert.All(invokerPaths, relativePath =>
        {
            var fullPath = Path.Combine(repositoryRoot.FullName, relativePath);
            Assert.True(File.Exists(fullPath), $"Missing source file for resumable super setup gate: {relativePath}");
            var source = File.ReadAllText(fullPath);
            Assert.DoesNotContain("GetOrCreateExecutionEnvironmentForInternalUse", source, StringComparison.Ordinal);
        });
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertAsyncGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private static ExecutionPlan GetClassMethodPlan(string source, string className, string methodName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<ClassDeclaration>(
            pipeline.Analyzed.Body.Single(statement =>
                statement is ClassDeclaration candidate &&
                candidate.Name.Name == className));
        var member = Assert.Single(declaration.Definition.Members.Where(candidate => candidate.Name == methodName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)member.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
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

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClassExpressionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicInstanceFields_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    first = 2;
                    empty;
                    second = this.first + 1;
                    constructor(value) {
                        this.ctor = value;
                    }
                };
                var c = new C(7);
                yield c.second;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicInstanceFields_RoutesResumableAndInitializesFields()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 0;
                var C = class {
                    first = 2;
                    empty;
                    second = this.first + 1;
                    constructor(value) {
                        this.ctor = value;
                        this.order = this.first + ":" + this.second;
                    }
                };
                var c = new C(7);
                yield c.first + "|" + c.empty + "|" + c.second + "|" + c.ctor + "|" + c.order;
            }

            var it = g();
            var beforeClass = it.next().value;
            var afterClass = it.next().value;
            beforeClass + "|" + afterClass;
            """);

        Assert.Equal("0|2|undefined|3|7|2:3", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Theory]
    [InlineData("""
        function* g() {
            yield class {
                constructor() {
                    this.value = 1;
                }
            };
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield class {
                field = seed;
            };
        }
        """)]
    [InlineData("""
        function* g() {
            yield class {
                static value = 1;
                field = 2;
            };
        }
        """)]
    [InlineData("""
        function* g(key) {
            yield class {
                [key] = 1;
                field = 2;
            };
        }
        """)]
    [InlineData("""
        function* g() {
            yield class {
                field = 1;
                get value() {
                    return 2;
                }
            };
        }
        """)]
    [InlineData("""
        function* g() {
            yield class {
                #value = 1;
                field = 2;
            };
        }
        """)]
    public void EvaluateResumable_ClassExpressionNonB24bShapes_Decline(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}

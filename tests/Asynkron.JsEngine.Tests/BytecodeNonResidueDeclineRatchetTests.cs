using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class BytecodeNonResidueDeclineRatchetTests
{
    private const int ExpectedKnownOpenNonResidueCount = 2;

    private static readonly RatchetRow[] Rows =
    [
        new(
            "D5_Admitted_SyncFunction",
            """
            function d5Sync(value) {
                var next = value + 1;
                return next * 2;
            }
            """,
            SubjectKind.Function,
            "d5Sync",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "D5_Admitted_Script",
            """
            1 + 2;
            """,
            SubjectKind.Script,
            string.Empty,
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "D5_Admitted_ResumableGenerator",
            """
            function* d5Generator() {
                yield 1;
                return 2;
            }
            """,
            SubjectKind.ResumableGenerator,
            "d5Generator",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "D5_Admitted_ResumableAsync",
            """
            async function d5Async(value) {
                var next = await value;
                return next + 1;
            }
            """,
            SubjectKind.ResumableAsync,
            "d5Async",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "A32_ChainedOptionalComputedDelete_Declines",
            """
            function d5A32(box, first, second) {
                return delete box?.[first][second];
            }
            """,
            SubjectKind.Function,
            "d5A32",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.OptionalChainDependency),
        new(
            "B8a_LexicalSlotAssignmentInResumable_Admitted",
            """
            function* d5B8a() {
                let value = 0;
                yield value;
                value = 1;
                yield value;
            }
            """,
            SubjectKind.ResumableGenerator,
            "d5B8a",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "B23_B36_NestedFunctionDeclarationInResumable_Declines",
            """
            function* d5B23B36() {
                function helper() {
                    return 1;
                }

                yield helper();
            }
            """,
            SubjectKind.ResumableGenerator,
            "d5B23B36",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape),
        new(
            "B26_FreeWriteInResumable_Admitted",
            """
            var outer = 0;
            function* d5B26() {
                yield outer;
                outer = yield 1;
                yield outer;
            }
            """,
            SubjectKind.ResumableGenerator,
            "d5B26",
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None)
    ];

    public static TheoryData<string, string, SubjectKind, string, bool, int> CorpusRows
    {
        get
        {
            var rows = new TheoryData<string, string, SubjectKind, string, bool, int>();
            foreach (var row in Rows)
            {
                rows.Add(
                    row.Name,
                    row.Source,
                    row.SubjectKind,
                    row.FunctionName,
                    row.IsDynamicResidue,
                    (int)row.ExpectedDeclineCode);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(CorpusRows))]
    public void CorpusRows_MatchCurrentProductionEligibility(
        string name,
        string source,
        SubjectKind subjectKind,
        string functionName,
        bool isDynamicResidue,
        int expectedDeclineCodeValue)
    {
        Assert.False(isDynamicResidue);

        var expectedDeclineCode = (UnifiedBytecodeProductionDeclineCode)expectedDeclineCodeValue;
        var result = Evaluate(source, subjectKind, functionName);
        if (expectedDeclineCode == UnifiedBytecodeProductionDeclineCode.None)
        {
            Assert.True(result.IsEligible, $"{name}: {result.Code} {result.Reason}");
            Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
            return;
        }

        Assert.False(result.IsEligible, $"{name}: unexpectedly admitted.");
        Assert.Equal(expectedDeclineCode, result.Code);
    }

    [Fact]
    public void KnownOpenNonResidueCount_IsPinned()
    {
        var knownOpenCount = Rows.Count(
            static row => !row.IsDynamicResidue &&
                          row.ExpectedDeclineCode != UnifiedBytecodeProductionDeclineCode.None);

        Assert.Equal(ExpectedKnownOpenNonResidueCount, knownOpenCount);
    }

    private static UnifiedBytecodeProductionEligibilityResult Evaluate(
        string source,
        SubjectKind subjectKind,
        string functionName)
    {
        return subjectKind switch
        {
            SubjectKind.Function => UnifiedBytecodeProductionEligibility.Evaluate(
                GetFunctionPlan(source, functionName),
                new UnifiedBytecodeProductionActivationDescriptor()),
            SubjectKind.Script => UnifiedBytecodeProductionEligibility.EvaluateScript(GetScriptPlan(source)),
            SubjectKind.ResumableGenerator => UnifiedBytecodeProductionEligibility.EvaluateResumable(
                GetFunctionPlan(source, functionName),
                new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true)),
            SubjectKind.ResumableAsync => UnifiedBytecodeProductionEligibility.EvaluateResumable(
                GetFunctionPlan(source, functionName),
                new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(subjectKind), subjectKind, null)
        };
    }

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new InvalidOperationException("Function rows must provide a function name.");
        }

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static ExecutionPlan GetScriptPlan(string source)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var cache = ((IAstCacheable<ScriptPlanCache>)pipeline.Analyzed).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    public enum SubjectKind
    {
        Function,
        Script,
        ResumableGenerator,
        ResumableAsync
    }

    private sealed record RatchetRow(
        string Name,
        string Source,
        SubjectKind SubjectKind,
        string FunctionName,
        bool IsDynamicResidue,
        UnifiedBytecodeProductionDeclineCode ExpectedDeclineCode);
}

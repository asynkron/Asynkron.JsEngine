using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class BytecodeNonResidueDeclineRatchetTests
{
    private const int ExpectedKnownOpenNonResidueCount = 3;

    private static readonly string[] KnownOpenNonResidueDeclines =
    [
        "A32_ChainedOptionalComputedDelete_Declines",
        "B23_B36_NestedFunctionDeclarationInResumable_Declines",
        "C3_TopLevelLexicalDestructuring_Declines"
    ];

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
            "C3_Admitted_ComposedScript",
            """
            var source = { head: 2, tail: 3, extra: 5 };
            var { head, ...rest } = source;
            let box = { value: head, add(n) { return this.value + n; } };
            let total = Math.pow(box.add(rest.tail), 2);

            if (rest.extra > 4) {
                total += Math.sqrt(16);
            }

            total;
            """,
            SubjectKind.Script,
            string.Empty,
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "C3_TopLevelLexicalDestructuring_Declines",
            """
            const source = { value: 1 };
            const { value } = source;
            value;
            """,
            SubjectKind.Script,
            string.Empty,
            IsDynamicResidue: false,
            UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape),
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
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "D5_Residue_DirectEvalCall_Admitted",
            """
            function d5DirectEval() {
                return eval("1 + 2");
            }
            """,
            SubjectKind.Function,
            "d5DirectEval",
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.None),
        new(
            "D5_Residue_DirectEvalMultiArg_Declines",
            """
            function d5DirectEvalMultiArg() {
                return eval("1 + 2", "ignored");
            }
            """,
            SubjectKind.Function,
            "d5DirectEvalMultiArg",
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.CallDependency),
        new(
            "D5_Residue_DirectEvalSpread_Declines",
            """
            function d5DirectEvalSpread(parts) {
                return eval(...parts);
            }
            """,
            SubjectKind.Function,
            "d5DirectEvalSpread",
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.CallDependency),
        new(
            "D5_Residue_EvalInjectedRuntimeBinding_Declines",
            """
            function d5EvalInjectedRuntimeBinding() {
                eval("var injected = 1");
                return injected;
            }
            """,
            SubjectKind.Function,
            "d5EvalInjectedRuntimeBinding",
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency),
        new(
            "C3_Residue_ScriptEvalInjectedRuntimeBinding_Declines",
            """
            eval("var injected = 1");
            injected;
            """,
            SubjectKind.Script,
            string.Empty,
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.CallDependency),
        new(
            "D5_Residue_ResumableWithDynamicScope_Declines",
            """
            function* d5ResumableWithDynamicScope(obj) {
                with (obj) {
                    yield value;
                }
            }
            """,
            SubjectKind.ResumableGenerator,
            "d5ResumableWithDynamicScope",
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape),
        new(
            "D5_Residue_AwaitedWithObject_Declines",
            """
            async function d5AwaitedWithObject(obj) {
                with (await obj) {
                    return value;
                }
            }
            """,
            SubjectKind.ResumableAsync,
            "d5AwaitedWithObject",
            IsDynamicResidue: true,
            UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape)
    ];

    // Function-constructor-produced bodies are runtime-created source and are not represented by the
    // current ExecutionPlan-only eligibility helpers. The Function call boundary itself is not residue.

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

        if (isDynamicResidue)
        {
            return;
        }

        Assert.Contains(name, KnownOpenNonResidueDeclines);
    }

    [Fact]
    public void KnownOpenNonResidueCount_IsPinned()
    {
        var knownOpenCount = Rows.Count(
            static row => !row.IsDynamicResidue &&
                          row.ExpectedDeclineCode != UnifiedBytecodeProductionDeclineCode.None);

        Assert.Equal(ExpectedKnownOpenNonResidueCount, knownOpenCount);
    }

    [Fact]
    public void NonResidueDeclines_AreOnlyKnownOpenRows()
    {
        var nonResidueDeclines = Rows
            .Where(static row => !row.IsDynamicResidue &&
                                 row.ExpectedDeclineCode != UnifiedBytecodeProductionDeclineCode.None)
            .Select(static row => row.Name)
            .ToArray();

        Assert.Equal(KnownOpenNonResidueDeclines.Order(), nonResidueDeclines.Order());
    }

    [Fact]
    public void DynamicResidueCorpus_IsExplicit()
    {
        var dynamicResidueCount = Rows.Count(static row => row.IsDynamicResidue);

        Assert.Equal(7, dynamicResidueCount);
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

using System.Linq;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Xunit;
using Xunit.Abstractions;
namespace Asynkron.JsEngine.Tests;
public sealed class ZZProbeTryFinally(ITestOutputHelper o) : InternalTestBase(o) {
    [Fact]
    public void DumpPlan() {
        var pipeline = AstTestHelpers.ParseAndAnalyze("function* cleanup(){ try { yield 1; } finally { yield 2; } }");
        var decl = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body.Single(n => n is FunctionDeclaration f && f.Name?.Name == "cleanup"));
        var cache = ((IAstCacheable<ExecutionPlanCache>)decl.Function).GetOrCreateCache();
        var plan = Assert.IsType<ExecutionPlan>(cache.Plan);
        var sb = new StringBuilder($"\nEntryPoint={plan.EntryPoint} count={plan.Instructions.Length}\n");
        for (var i = 0; i < plan.Instructions.Length; i++) {
            var ins = plan.Instructions[i];
            var extra = ins is EnterTryInstruction et ? $" [Finally={et.FinallyIndex} LeaveTry={et.LeaveTryIndex} EndFinally={et.EndFinallyIndex}]" : "";
            sb.Append($"{i}: {ins.GetType().Name}{extra} Next={ins.Next}\n");
        }
        Assert.True(false, sb.ToString());
    }
}

using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class DebugSlotTest
{
    private readonly ITestOutputHelper _output;
    public DebugSlotTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task PrintSlotInfo()
    {
        await using var engine = new JsEngine();
        var script = @"
(function() {
    let sum = 0;
    const arr = [1, 2, 3];
    for (let i = 0; i < 3; i++) {
        for (const n of arr) {
            sum += n;
        }
    }
    return sum;
})();
";
        var parsed = engine.ParseProgram(script);
        PrintNode(parsed, 0);
    }

    void PrintNode(AstNode node, int indent)
    {
        var prefix = new string(' ', indent * 2);
        switch (node)
        {
            case ProgramNode pn:
                _output.WriteLine($"{prefix}Program: ScopeId={pn.ScopeId}, SlotCount={pn.SlotCount}");
                foreach (var stmt in pn.Body) PrintNode(stmt, indent + 1);
                break;
            case FunctionExpression fe:
                _output.WriteLine($"{prefix}FunctionExpression: ScopeId={fe.ScopeId}, SlotCount={fe.SlotCount}");
                PrintNode(fe.Body, indent + 1);
                break;
            case BlockStatement bs:
                _output.WriteLine($"{prefix}BlockStatement: ScopeId={bs.ScopeId}, SlotCount={bs.SlotCount}");
                foreach (var stmt in bs.Statements) PrintNode(stmt, indent + 1);
                break;
            case ForStatement fs:
                _output.WriteLine($"{prefix}ForStatement: PerIterationScopeId={fs.PerIterationScopeId}, LoopEnvSlotIndex={fs.LoopEnvSlotIndex}, LoopEnvScopeId={fs.LoopEnvScopeId}");
                PrintNode(fs.Body, indent + 1);
                break;
            case ForEachStatement fes:
                _output.WriteLine($"{prefix}ForEachStatement: PerIterationScopeId={fes.PerIterationScopeId}, LoopEnvSlotIndex={fes.LoopEnvSlotIndex}, LoopEnvScopeId={fes.LoopEnvScopeId}");
                PrintNode(fes.Body, indent + 1);
                break;
            case ExpressionStatement es:
                PrintNode(es.Expression, indent);
                break;
            case CallExpression ce:
                PrintNode(ce.Callee, indent);
                break;
            case ReturnStatement rs:
                _output.WriteLine($"{prefix}ReturnStatement");
                break;
            case VariableDeclaration vd:
                _output.WriteLine($"{prefix}VariableDeclaration: Kind={vd.Kind}");
                break;
            default:
                _output.WriteLine($"{prefix}{node.GetType().Name}");
                break;
        }
    }
}

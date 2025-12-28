using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class EvalFunctionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Eval_EvaluatesSimpleExpression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       eval('2 + 2;');

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_AccessesVariablesInScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let x = 10;
                                                       eval('x + 5;');

                                           """);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_CreatesVariables()
    {
        await using var engine = CreateEngine();
        // Note: Only 'var' declarations in eval are visible in the enclosing scope.
        // 'let' and 'const' in eval are block-scoped to the eval and not accessible outside.
        var result = await engine.Evaluate("""

                                                       eval('var y = 42;');
                                                       y;

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       eval('"hello" + " " + "world";');

                                           """);
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithFunction()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       eval('function add(a, b) { return a + b; }');
                                                       add(3, 7);

                                           """);
        Assert.Equal(10d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Eval_WithNonStringReturnsValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       eval(42);

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithNoArgumentsReturnsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       eval();

                                           """);
        Assert.True(ReferenceEquals(result, Symbol.Undefined));
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithComplexExpression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let base = 5;
                                                       let exp = eval('base * base;');
                                                       exp;

                                           """);
        Assert.Equal(25d, result);
    }

//     [Fact(Timeout = 2000)]
//     public async Task Eval_WithArrayExpression()
//     {
//         await using var engine = CreateEngine();
//         // Array literals need to be properly parenthesized when used with eval
//         var result = await engine.Evaluate("""
//
//                                                        let arr = eval('([1, 2, 3, 4, 5]);');
//                                                        arr[2];
//
//                                            """);
//         Assert.Equal(3d, result);
//     }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithObjectExpression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = eval('({ x: 10, y: 20 });');
                                                       obj.x + obj.y;

                                           """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_VarInForOfIterable_ModifiesOuterScope()
    {
        // Test262: language/statements/for-of/scope-head-var-none.js
        // When eval('var x = 2;') is in the for-of iterable expression,
        // the var should be created in the outer variable environment, not the TDZ.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = 1;
            for (let [_] of [[eval('var x = 2;')]]) { }
            x;
        """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_VarInForOfIterable_ClosuresSeeCurrent()
    {
        // Test262: language/statements/for-of/scope-head-var-none.js (full version with closures)
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var probeBefore = function() { return x; };
            var x = 1;
            var probeDecl, probeExpr, probeBody;

            for (
                let [_ = probeDecl = function() { return x; }]
                of
                [[eval('var x = 2;'), probeExpr = function() { return x; }]]
              )
              probeBody = function() { return x; };

            JSON.stringify([probeBefore(), probeDecl(), probeExpr(), probeBody(), x]);
        """);
        // All should be 2 since eval modified the outer x
        Assert.Equal("[2,2,2,2,2]", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_ClosureInIterable_SeesOuterScope_NoEval()
    {
        // Simpler test without eval - closure in iterable expression should see outer var x
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = 42;
            var probe;
            for (let [_] of [[1, probe = function() { return x; }]]) { }
            probe();
        """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_ClosureInDestructuringDefault_SeesLoopVar()
    {
        // Test262: language/statements/for-of/scope-body-lex-open.js
        // Closure in destructuring default should see loop variable value
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var probe;
            for (let [x, _ = probe = function() { return x; }] of [['inside']]) { }
            probe();
        """);
        Assert.Equal("inside", result);
    }
}

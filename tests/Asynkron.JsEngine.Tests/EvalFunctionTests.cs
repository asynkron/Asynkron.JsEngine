using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class EvalFunctionTests(ITestOutputHelper output) : InternalTestBase(output)
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

    [Fact(Timeout = 5000)]
    public async Task Eval_VarInit_LocalNewDelete()
    {
        // Test eval var hoisting - x should be undefined before var statement
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var initial = null;
            (function() {
              eval('initial = x; var x;');
            }());
            initial;
        """);
        Output.WriteLine($"initial = {result}");
        Assert.True(ReferenceEquals(result, Symbol.Undefined), $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_VarDelete_NewBinding()
    {
        // Test that new var binding created by eval in function scope is deletable
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var deleteResult = null;
            (function() {
              eval('var y = 1; deleteResult = delete y;');
            }());
            deleteResult;
        """);
        Output.WriteLine($"delete result = {result}");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_VarDeleteThenAccess_ShouldThrow()
    {
        // Test that after deleting eval-created var, access throws ReferenceError
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var threw = false;
            (function() {
              eval('var x = 1; delete x;');
              try {
                x;
              } catch (e) {
                threw = e instanceof ReferenceError;
              }
            }());
            threw;
        """);
        Output.WriteLine($"threw ReferenceError = {result}");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_VarInit_LocalNewDelete_Full()
    {
        // Full test matching Test262 var-env-var-init-local-new-delete.js
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var initial = null;
            var postDeletion;

            (function() {
              eval('initial = x; delete x; postDeletion = function() { x; }; var x;');
            }());

            // Check that initial was undefined (var hoisting)
            if (initial !== undefined) throw new Error('initial should be undefined, got: ' + initial);

            // Check that postDeletion throws ReferenceError (binding was deleted)
            var threwError = false;
            try {
                postDeletion();
            } catch (e) {
                if (e instanceof ReferenceError) {
                    threwError = true;
                }
            }
            if (!threwError) throw new Error('postDeletion should throw ReferenceError');
            'success';
        """);
        Output.WriteLine($"result = {result}");
        Assert.Equal("success", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_ClosureAccessToDeletedVar()
    {
        // Test that a closure defined in eval throws when accessing a deleted variable
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var capture;
            var threw = false;
            (function() {
              eval('var x = 1; capture = function() { return x; }; delete x;');
              try {
                capture();
              } catch (e) {
                threw = e instanceof ReferenceError;
              }
            }());
            threw;
        """);
        Output.WriteLine($"threw ReferenceError = {result}");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_ClosureDefinedAfterDelete()
    {
        // Test that a closure defined AFTER delete still throws ReferenceError
        // This matches the Test262 test pattern
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var capture;
            var threw = false;
            (function() {
              eval('delete x; capture = function() { x; }; var x;');
              try {
                capture();
              } catch (e) {
                threw = e instanceof ReferenceError;
              }
            }());
            threw;
        """);
        Output.WriteLine($"threw ReferenceError = {result}");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Eval_VarBindingDeletedStaysDeleted()
    {
        // ES2024 19.2.1.3: var bindings created by EvalDeclarationInstantiation
        // should NOT be re-created when the var statement is encountered after delete.
        // This tests that accessing x after the var statement still throws ReferenceError.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var xBefore, deleteResult, xAfterDeleteThrew, xAfterVarThrew;
            (function() {
              eval(`
                xBefore = typeof x;       // 'undefined' due to hoisting
                deleteResult = delete x;  // should return true

                // Access x after delete (should throw ReferenceError)
                try {
                  x;
                  xAfterDeleteThrew = false;
                } catch (e) {
                  xAfterDeleteThrew = e instanceof ReferenceError;
                }

                var x;  // Should NOT re-create the deleted binding

                // Access x after the var statement (should STILL throw ReferenceError)
                try {
                  x;
                  xAfterVarThrew = false;
                } catch (e) {
                  xAfterVarThrew = e instanceof ReferenceError;
                }
              `);
            }());
            [xBefore, deleteResult, xAfterDeleteThrew, xAfterVarThrew].join(', ');
        """);
        Output.WriteLine($"result = {result}");
        Assert.Equal("undefined, true, true, true", result);
    }
}

using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for flat slot optimization (issue #359).
/// These tests verify correct variable access semantics that must be preserved
/// when implementing flat slot indexing for O(1) variable access.
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
public sealed class FlatSlotOptimizationTests : InternalTestBase
{
    public FlatSlotOptimizationTests(ITestOutputHelper output) : base(output)
    {
    }

    #region Simple Variable Access

    [Fact]
    public async Task SimpleLetAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 42;
            x;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task SimpleConstAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            const x = 42;
            x;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task SimpleVarAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            var x = 42;
            x;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task MultipleVariablesInSameScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let a = 1;
            let b = 2;
            let c = 3;
            a + b + c;
            """);

        Assert.Equal(6.0, result);
    }

    [Fact]
    public async Task VariableReassignment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 1;
            x = 2;
            x = 3;
            x;
            """);

        Assert.Equal(3.0, result);
    }

    [Fact]
    public async Task CompoundAssignment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 10;
            x += 5;
            x -= 3;
            x *= 2;
            x;
            """);

        Assert.Equal(24.0, result);
    }

    [Fact]
    public async Task IncrementDecrement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 0;
            x++;
            x++;
            ++x;
            x--;
            x;
            """);

        Assert.Equal(2.0, result);
    }

    #endregion

    #region Nested Blocks with Shadowing

    [Fact]
    public async Task BlockShadowingPreservesOuterValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 1;
            {
                let x = 2;
            }
            x;
            """);

        Assert.Equal(1.0, result);
    }

    [Fact]
    public async Task NestedBlockShadowing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let results = [];
            let x = 1;
            results.push(x);
            {
                let x = 2;
                results.push(x);
                {
                    let x = 3;
                    results.push(x);
                }
                results.push(x);
            }
            results.push(x);
            results.join(',');
            """);

        Assert.Equal("1,2,3,2,1", result);
    }

    [Fact]
    public async Task BlockShadowingWithDifferentTypes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 'outer';
            {
                let x = 42;
                if (x !== 42) throw new Error('inner should be 42');
            }
            x;
            """);

        Assert.Equal("outer", result);
    }

    [Fact]
    public async Task MultipleShadowedVariables()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let a = 1, b = 2;
            {
                let a = 10, b = 20;
                if (a + b !== 30) throw new Error('inner sum should be 30');
            }
            a + b;
            """);

        Assert.Equal(3.0, result);
    }

    [Fact]
    public async Task ShadowingInFunctionInsideBlock()
    {
        // Node.js correctly returns "inner" for inner(), but engine returns "outer"
        // This is a bug in nested function declaration scope handling
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function outer() {
                let x = 'outer';
                function inner() {
                    let x = 'inner';
                    return x;
                }
                return inner();
            }
            outer();
            """);

        Assert.Equal("inner", result);
    }

    #endregion

    #region Loops with Block-Scoped Variables

    [Fact]
    public async Task ForLoopWithLetCounter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (let i = 0; i < 5; i++) {
                sum += i;
            }
            sum;
            """);

        Assert.Equal(10.0, result);
    }

    [Fact]
    public async Task ForLoopCounterNotVisibleOutside()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            for (let i = 0; i < 3; i++) {}
            typeof i;
            """);

        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task NestedForLoops()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 3; j++) {
                    sum += i * 10 + j;
                }
            }
            sum;
            """);

        // 00+01+02 + 10+11+12 + 20+21+22 = 0+1+2 + 10+11+12 + 20+21+22 = 99
        Assert.Equal(99.0, result);
    }

    [Fact]
    public async Task ForLoopWithBlockScopedVar()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let results = [];
            for (let i = 0; i < 3; i++) {
                let doubled = i * 2;
                results.push(doubled);
            }
            results.join(',');
            """);

        Assert.Equal("0,2,4", result);
    }

    [Fact]
    public async Task WhileLoopWithBlockVar()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            let i = 0;
            while (i < 5) {
                let increment = i;
                sum += increment;
                i++;
            }
            sum;
            """);

        Assert.Equal(10.0, result);
    }

    [Fact]
    public async Task ForOfWithBlockVar()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (let x of [1, 2, 3, 4, 5]) {
                sum += x;
            }
            sum;
            """);

        Assert.Equal(15.0, result);
    }

    [Fact]
    public async Task LoopWithShadowingOuterVar()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 'outer';
            for (let i = 0; i < 3; i++) {
                let x = i;
            }
            x;
            """);

        Assert.Equal("outer", result);
    }

    #endregion

    #region Function Parameters

    [Fact]
    public async Task FunctionParameterAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function add(a, b) {
                return a + b;
            }
            add(3, 4);
            """);

        Assert.Equal(7.0, result);
    }

    [Fact]
    public async Task FunctionParameterShadowing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 'outer';
            function test(x) {
                return x;
            }
            test('inner') + ',' + x;
            """);

        Assert.Equal("inner,outer", result);
    }

    [Fact]
    public async Task FunctionParameterModification()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function modify(x) {
                x = x + 10;
                return x;
            }
            modify(5);
            """);

        Assert.Equal(15.0, result);
    }

    [Fact]
    public async Task DefaultParameterValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function greet(name = 'World') {
                return 'Hello, ' + name;
            }
            greet() + ' | ' + greet('Test');
            """);

        Assert.Equal("Hello, World | Hello, Test", result);
    }

    [Fact]
    public async Task RestParameters()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function sum(...nums) {
                let total = 0;
                for (let n of nums) total += n;
                return total;
            }
            sum(1, 2, 3, 4, 5);
            """);

        Assert.Equal(15.0, result);
    }

    #endregion

    #region Closures Capturing Outer Variables

    [Fact]
    public async Task SimpleClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function makeCounter() {
                let count = 0;
                return function() {
                    count++;
                    return count;
                };
            }
            let counter = makeCounter();
            counter() + ',' + counter() + ',' + counter();
            """);

        Assert.Equal("1,2,3", result);
    }

    [Fact]
    public async Task ClosureCapturingMultipleVars()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function makeAdder(x) {
                let offset = 100;
                return function(y) {
                    return x + y + offset;
                };
            }
            let add5 = makeAdder(5);
            add5(10);
            """);

        Assert.Equal(115.0, result);
    }

    [Fact]
    public async Task NestedClosures()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function outer(a) {
                return function middle(b) {
                    return function inner(c) {
                        return a + b + c;
                    };
                };
            }
            outer(1)(2)(3);
            """);

        Assert.Equal(6.0, result);
    }

    [Fact]
    public async Task ClosureInLoop_PerIterationBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let funcs = [];
            for (let i = 0; i < 3; i++) {
                funcs.push(() => i);
            }
            funcs[0]() + ',' + funcs[1]() + ',' + funcs[2]();
            """);

        Assert.Equal("0,1,2", result);
    }

    [Fact]
    public async Task ClosureCapturingBlockVar()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let getX;
            {
                let x = 'block value';
                getX = () => x;
            }
            getX();
            """);

        Assert.Equal("block value", result);
    }

    [Fact]
    public async Task ClosureModifyingCapturedVar()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function makeAccumulator() {
                let sum = 0;
                return {
                    add: (x) => { sum += x; },
                    get: () => sum
                };
            }
            let acc = makeAccumulator();
            acc.add(5);
            acc.add(10);
            acc.add(3);
            acc.get();
            """);

        Assert.Equal(18.0, result);
    }

    #endregion

    #region With Statement (Should Use Scope Chain Fallback)

    [Fact]
    public async Task WithStatementBasic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let obj = { x: 42 };
            let x = 1;
            with (obj) {
                x;
            }
            """);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task WithStatementFallthrough()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let obj = { y: 100 };
            let x = 42;
            with (obj) {
                x;
            }
            """);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task WithStatementNestedAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let outer = 1;
            let obj = { inner: 2 };
            with (obj) {
                outer + inner;
            }
            """);

        Assert.Equal(3.0, result);
    }

    #endregion

    #region Eval (Should Use Scope Chain Fallback)

    [Fact]
    public async Task DirectEvalAccessesLocalScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function test() {
                let x = 42;
                return eval('x');
            }
            test();
            """);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task DirectEvalCanCreateLocalBindings()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function test() {
                eval('var y = 99');
                return y;
            }
            test();
            """);

        Assert.Equal(99.0, result);
    }

    #endregion

    #region Mixed Scenarios

    [Fact]
    public async Task ComplexNestedScopesWithClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function outer() {
                let a = 1;
                {
                    let b = 2;
                    {
                        let c = 3;
                        return function() {
                            return a + b + c;
                        };
                    }
                }
            }
            outer()();
            """);

        Assert.Equal(6.0, result);
    }

    [Fact]
    public async Task LoopWithClosureAndShadowing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            let x = 'outer';
            let funcs = [];
            for (let i = 0; i < 3; i++) {
                let x = i * 10;
                funcs.push(() => x);
            }
            x + ',' + funcs[0]() + ',' + funcs[1]() + ',' + funcs[2]();
            """);

        Assert.Equal("outer,0,10,20", result);
    }

    [Fact]
    public async Task FunctionReturningClosureFromBlock()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function make(n) {
                if (n > 0) {
                    let positive = n;
                    return () => positive;
                } else {
                    let negative = n;
                    return () => negative;
                }
            }
            make(5)() + ',' + make(-3)();
            """);

        Assert.Equal("5,-3", result);
    }

    [Fact]
    public async Task HighIterationLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let sum = 0;
                for (let i = 0; i < 10000; i++) {
                    sum += i;
                }
                return sum;
            }
            run();
            """);

        // Sum of 0..9999 = 9999 * 10000 / 2 = 49995000
        Assert.Equal(49995000.0, result);
    }

    [Fact]
    public async Task ManyLocalVariables()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';
            function test() {
                let a = 1, b = 2, c = 3, d = 4, e = 5;
                let f = 6, g = 7, h = 8, i = 9, j = 10;
                return a + b + c + d + e + f + g + h + i + j;
            }
            test();
            """);

        Assert.Equal(55.0, result);
    }

    #endregion
}

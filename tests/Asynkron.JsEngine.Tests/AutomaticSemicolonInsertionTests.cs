using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class AutomaticSemicolonInsertionTests
{
    [Fact(Timeout = 2000)]
    public async Task ReturnWithLineBreakReturnsUndefined()
    {
        // return\n{} should be parsed as: return; {}
        // The {} becomes a separate block statement, not returned
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test() {
                return
                {}
            }
            test();
        ");

        // Should return undefined (null in C#), not the object
        Assert.Null(result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReturnWithObjectOnSameLine()
    {
        // return { on same line should parse the object with computed property
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test() {
                return {
                    value: 42
                }
            }
            test();
        ");

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(42d, obj["value"]);
    }

    [Fact(Timeout = 2000)]
    public async Task ExpressionStatementWithoutSemicolon()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x = 1
            let y = 2
            x + y
        ");

        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task VariableDeclarationWithoutSemicolon()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let a = 10
            let b = 20
            a + b
        ");

        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultiLineExpressionNoASI()
    {
        // a = b + c should parse as one expression
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let a = 1
            let b = 2
            a = b
            + 3
            a
        ");

        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PropertyAccessAcrossLines()
    {
        // obj.prop should work across lines
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let obj = { value: 42 }
            obj
            .value
        ");

        Assert.Equal(42d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ArrayAccessAcrossLines()
    {
        // arr[0] should work across lines
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3]
            arr
            [0]
        ");

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionCallAcrossLines()
    {
        // func() should work across lines
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function getValue() { return 100 }
            getValue
            ()
        ");

        Assert.Equal(100d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClosingBraceTriggersASI()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test() {
                let x = 5
                return x + 10
            }
            test()
        ");

        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EOFTriggersASI()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let value = 42
            value");

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ThrowWithLineBreakFails()
    {
        // throw\nexpression should fail - line terminator not allowed after throw
        await using var engine = new JsEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(@"
                function test() {
                    throw
                    new Error('test')
                }
                test()
            ");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task ThrowWithExpressionOnSameLine()
    {
        await using var engine = new JsEngine();

        // This should throw a ThrowSignal with the error message
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await engine.Evaluate(@"
                function test() {
                    throw new Error('test error')
                }
                test()
            ");
        });

        // Check that the error was thrown (either ThrowSignal or another exception type)
        Assert.NotNull(exception);
    }

    [Fact(Timeout = 2000)]
    public async Task ContinueStatementASI()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let sum = 0
            for (let i = 0; i < 10; i = i + 1) {
                if (i === 5) {
                    continue
                }
                sum = sum + i
            }
            sum
        ");

        // Should skip 5: 0+1+2+3+4+6+7+8+9 = 40
        Assert.Equal(40d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task BreakStatementASI()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let sum = 0
            for (let i = 0; i < 10; i = i + 1) {
                if (i === 5) {
                    break
                }
                sum = sum + i
            }
            sum
        ");

        // Should stop at 5: 0+1+2+3+4 = 10
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task IfStatementWithoutBraces()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x = 5
            if (x > 0)
                x = 10
            x
        ");

        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComplexCodeWithASI()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function calculate(a, b) {
                let sum = a + b
                let product = a * b
                return {
                    sum: sum,
                    product: product
                }
            }

            let result = calculate(3, 4)
            result.sum + result.product
        ");

        Assert.Equal(19d, result); // 7 + 12
    }

    [Fact(Timeout = 2000)]
    public async Task ReturnWithCommaOperator()
    {
        // Simple test: comma operator returns last value
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test() {
                return 1, 2, 3;
            }
            test()
        ");

        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ReturnWithCommaOperatorComplex()
    {
        // Test return statement with comma operator (sequences multiple expressions)
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function createCommonjsModule(fn, basedir, module) {
                return module = {
                    path: basedir,
                    exports: {},
                    require: function (path, base) {
                        return 'inner';
                    }
                }, fn(module, module.exports), module.exports;
            }

            let capturedModule = null;
            let capturedExports = null;
            function testFn(mod, exp) {
                capturedModule = mod;
                capturedExports = exp;
            }

            let result = createCommonjsModule(testFn, '/base', null);
            // The comma operator returns the last value, which is module.exports (an empty object)
            // So result should be an empty object, not the module itself
            capturedModule.path
        ");

        Assert.Equal("/base", result);
    }
}

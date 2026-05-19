namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class LoopsTests(JsEngineTestFixture fixture) : JsEngineTestBase(fixture)
{
    // Const assignment error tests
    [Fact(Timeout = 2000)]
    public async Task ForLoop_ConstIncrement_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var caught = false;
            var errorType = null;
            var errorMsg = null;
            try {
                for (const i = 0; i < 1; i++) {}
            } catch (e) {
                caught = true;
                errorType = e.constructor ? e.constructor.name : typeof e;
                errorMsg = e.message || String(e);
            }
            JSON.stringify({ caught: caught, errorType: errorType, errorMsg: errorMsg });
            """);
        var json = result?.ToString() ?? "";
        Assert.Contains("\"caught\":true", json);
        Assert.Contains("\"errorType\":\"TypeError\"", json);
    }

    // Traditional for loop tests with comma expressions
    [Fact(Timeout = 2000)]
    public async Task ForLoopWithCommaInInitializer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let sum = 0;
                                                       for (i = 0, len = arr.length; i < len; i++) {
                                                           sum = sum + arr[i];
                                                       }
                                                       sum;

                                                   """);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForLoopWithMultipleCommaExpressionsInInitializer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let result = '';
                                                       for (a = 1, b = 2, c = 3; a < 3; a++) {
                                                           result = result + a + b + c;
                                                       }
                                                       result;

                                                   """);
        Assert.Equal("123223", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForLoopWithCommaExpressionAndComplexInitializer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [10, 20, 30, 40];
                                                       let total = 0;
                                                       for (i = 0, len = arr.length, multiplier = 2; i < len; i++) {
                                                           total = total + (arr[i] * multiplier);
                                                       }
                                                       total;

                                                   """);
        Assert.Equal(200d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForLoopWithCommaInCondition()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let sum = 0;
                                                       for (let i = 0; i < 3, i < 5; i++) {
                                                           sum = sum + i;
                                                       }
                                                       sum;

                                                   """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForLoopWithCommaInIncrement()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let sum = 0;
                                                       let i = 0;
                                                       let j = 0;
                                                       for (; i < 3; i++, j += 2) {
                                                           sum = sum + i + j;
                                                       }
                                                       sum;

                                                   """);
        Assert.Equal(9d, result);
    }

    // for...in loop tests
    [Fact(Timeout = 2000)]
    public async Task ForInLoopBasic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 1, b: 2, c: 3 };
                                                       let keys = '';
                                                       for (let key in obj) {
                                                           keys = keys + key;
                                                       }
                                                       keys;

                                           """);
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForInLoopWithValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 10, y: 20, z: 30 };
                                                       let sum = 0;
                                                       for (let key in obj) {
                                                           sum = sum + obj[key];
                                                       }
                                                       sum;

                                           """);
        Assert.Equal(60d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ForInLoopArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [10, 20, 30];
                                                       let indices = '';
                                                       for (let i in arr) {
                                                           indices = indices + i;
                                                       }
                                                       indices;

                                           """);
        Assert.Equal("012", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForInLoopWithBreak()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 1, b: 2, c: 3, d: 4 };
                                                       let result = '';
                                                       for (let key in obj) {
                                                           result = result + key;
                                                           if (key === 'b') {
                                                               break;
                                                           }
                                                       }
                                                       result;

                                           """);
        Assert.Equal("ab", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForInLoopWithContinue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 1, b: 2, c: 3, d: 4 };
                                                       let result = '';
                                                       for (let key in obj) {
                                                           if (key === 'b') {
                                                               continue;
                                                           }
                                                           result = result + key;
                                                       }
                                                       result;

                                           """);
        Assert.Equal("acd", result);
    }

    // for...of loop tests
    [Fact(Timeout = 2000)]
    public async Task ForOfLoopArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [10, 20, 30];
                                                       let sum = 0;
                                                       for (let value of arr) {
                                                           sum = sum + value;
                                                       }
                                                       sum;

                                           """);
        Assert.Equal(60d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = 'abc';
                                                       let result = '';
                                                       for (let char of str) {
                                                           result = result + char;
                                                       }
                                                       result;

                                           """);
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopWithBreak()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let sum = 0;
                                                       for (let value of arr) {
                                                           if (value > 3) {
                                                               break;
                                                           }
                                                           sum = sum + value;
                                                       }
                                                       sum;

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopWithContinue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let sum = 0;
                                                       for (let value of arr) {
                                                           if (value === 3) {
                                                               continue;
                                                           }
                                                           sum = sum + value;
                                                       }
                                                       sum;

                                           """);
        Assert.Equal(12d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopBindingCanBeRedeclaredAfterLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let values = [];
            for (let value of [1, 2, 3]) {
                values.push(value);
            }

            let { value, done } = { value: 4, done: true };
            values.join(',') + ':' + value + ':' + done;
            """);

        Assert.Equal("1,2,3:4:true", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopNested()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let matrix = [[1, 2], [3, 4]];
                                                       let sum = 0;
                                                       for (let row of matrix) {
                                                           for (let value of row) {
                                                               sum = sum + value;
                                                           }
                                                       }
                                                       sum;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopNestedBreakPreservesOuterBodyLexicalBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let values = [];
            for (let outer of [1]) {
                let supported = false;
                for (let inner of [1]) {
                    supported = true;
                    break;
                }

                values.push(supported);
            }
            values[0];
            """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopNestedBreakPreservesOuterIterationBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let values = [];
            for (let collation of ["phonebk"]) {
                for (let locale of ["de"]) {
                    values.push(collation + ":" + locale);
                    break;
                }
            }
            values[0];
            """);
        Assert.Equal("phonebk:de", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopNestedBreakPreservesOuterIterationBindingInObjectShorthand()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let values = [];
            for (let collation of ["phonebk"]) {
                for (let locale of ["de"]) {
                    let options = { collation };
                    values.push(options.collation + ":" + locale);
                    break;
                }
            }
            values[0];
            """);
        Assert.Equal("phonebk:de", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopNestedContinuePreservesOuterIterationBindingInObjectShorthand()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let values = [];
            for (let collation of ["phonebk"]) {
                for (let locale of ["en", "ar", "de"]) {
                    let options = { collation };
                    values.push(options.collation + ":" + locale);
                }
            }
            values.join(",");
            """);
        Assert.Equal("phonebk:en,phonebk:ar,phonebk:de", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopNextOuterIterationAfterInnerBreakUsesOuterIteratorValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let values = [];
            for (let collation of ["compat", "dict"]) {
                for (let locale of ["en", "ar", "de"]) {
                    values.push(collation + ":" + locale);
                    if (locale === "ar") {
                        break;
                    }
                }
            }
            values.join(",");
            """);
        Assert.Equal("compat:en,compat:ar,dict:en,dict:ar", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfLoopLabeledBreakOutOfOuterLoopClosesInnerIterator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let closed = 0;
            let iterable = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            return { value: 1, done: false };
                        },
                        return() {
                            closed++;
                            return {};
                        }
                    };
                }
            };

            outer: for (let outerValue of [1]) {
                for (let innerValue of iterable) {
                    break outer;
                }
            }

            closed;
            """);
        Assert.Equal(1d, result);
    }
}

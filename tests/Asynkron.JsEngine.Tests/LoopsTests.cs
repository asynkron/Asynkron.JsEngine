using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class LoopsTests
{
    // for...in loop tests
    [Fact]
    public void ForInLoopBasic()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 1, b: 2, c: 3 };
            let keys = '';
            for (let key in obj) {
                keys = keys + key;
            }
            keys;
        ");
        Assert.Equal("abc", result);
    }

    [Fact]
    public void ForInLoopWithValues()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10, y: 20, z: 30 };
            let sum = 0;
            for (let key in obj) {
                sum = sum + obj[key];
            }
            sum;
        ");
        Assert.Equal(60d, result);
    }

    [Fact]
    public void ForInLoopArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = [10, 20, 30];
            let indices = '';
            for (let i in arr) {
                indices = indices + i;
            }
            indices;
        ");
        Assert.Equal("012", result);
    }

    [Fact]
    public void ForInLoopWithBreak()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 1, b: 2, c: 3, d: 4 };
            let result = '';
            for (let key in obj) {
                result = result + key;
                if (key === 'b') {
                    break;
                }
            }
            result;
        ");
        Assert.Equal("ab", result);
    }

    [Fact]
    public void ForInLoopWithContinue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 1, b: 2, c: 3, d: 4 };
            let result = '';
            for (let key in obj) {
                if (key === 'b') {
                    continue;
                }
                result = result + key;
            }
            result;
        ");
        Assert.Equal("acd", result);
    }

    // for...of loop tests
    [Fact]
    public void ForOfLoopArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = [10, 20, 30];
            let sum = 0;
            for (let value of arr) {
                sum = sum + value;
            }
            sum;
        ");
        Assert.Equal(60d, result);
    }

    [Fact]
    public void ForOfLoopString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let str = 'abc';
            let result = '';
            for (let char of str) {
                result = result + char;
            }
            result;
        ");
        Assert.Equal("abc", result);
    }

    [Fact]
    public void ForOfLoopWithBreak()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = [1, 2, 3, 4, 5];
            let sum = 0;
            for (let value of arr) {
                if (value > 3) {
                    break;
                }
                sum = sum + value;
            }
            sum;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ForOfLoopWithContinue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = [1, 2, 3, 4, 5];
            let sum = 0;
            for (let value of arr) {
                if (value === 3) {
                    continue;
                }
                sum = sum + value;
            }
            sum;
        ");
        Assert.Equal(12d, result);
    }

    [Fact]
    public void ForOfLoopNested()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let matrix = [[1, 2], [3, 4]];
            let sum = 0;
            for (let row of matrix) {
                for (let value of row) {
                    sum = sum + value;
                }
            }
            sum;
        ");
        Assert.Equal(10d, result);
    }
}

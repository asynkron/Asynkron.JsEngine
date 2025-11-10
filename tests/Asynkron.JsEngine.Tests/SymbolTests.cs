namespace Asynkron.JsEngine.Tests;

public class SymbolTests
{
    [Fact]
    public async Task Symbol_Creates_Unique_Symbols()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s1 = Symbol();
            let s2 = Symbol();
            s1 === s2;
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Symbol_With_Description()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let sym = Symbol(""test"");
            typeof sym;
        ");
        Assert.Equal("symbol", result);
    }

    [Fact]
    public async Task Symbol_Typeof_Returns_Symbol()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            typeof Symbol();
        ");
        Assert.Equal("symbol", result);
    }

    [Fact]
    public async Task Symbol_For_Creates_Global_Symbol()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s1 = Symbol.for(""shared"");
            let s2 = Symbol.for(""shared"");
            s1 === s2;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Symbol_For_Different_Keys_Creates_Different_Symbols()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s1 = Symbol.for(""key1"");
            let s2 = Symbol.for(""key2"");
            s1 === s2;
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task Symbol_KeyFor_Returns_Key_For_Global_Symbol()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s = Symbol.for(""myKey"");
            Symbol.keyFor(s);
        ");
        Assert.Equal("myKey", result);
    }

    [Fact]
    public async Task Symbol_KeyFor_Returns_Undefined_For_Non_Global_Symbol()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s = Symbol();
            let key = Symbol.keyFor(s);
            typeof key;
        ");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task Symbol_Can_Be_Used_As_Object_Property_Key()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let sym = Symbol(""id"");
            let obj = {};
            obj[sym] = 42;
            obj[sym];
        ");
        Assert.Equal(42.0, result);
    }

    // Symbol properties should not be enumerable in for...in loops
    [Fact]
    public async Task Symbol_Properties_Are_Not_Enumerable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let sym = Symbol(""secret"");
            let obj = { name: ""test"" };
            obj[sym] = ""hidden"";
            
            let keys = [];
            for (let key in obj) {
                keys.push(key);
            }
            keys.length;
        ");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public async Task Symbol_Works_With_Undefined()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s = Symbol(undefined);
            typeof s;
        ");
        Assert.Equal("symbol", result);
    }

    [Fact]
    public async Task Multiple_Global_Symbols_Work_Correctly()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let s1 = Symbol.for(""a"");
            let s2 = Symbol.for(""b"");
            let s3 = Symbol.for(""a"");
            
            let match1 = s1 === s3;
            let match2 = s1 === s2;
            
            match1 && !match2;
        ");
        Assert.True((bool)result!);
    }
}

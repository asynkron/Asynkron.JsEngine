using Xunit;

namespace Asynkron.JsEngine.Tests;

public class DeleteOperatorTests
{
    [Fact(Timeout = 2000)]
    public async Task Delete_RemovesPropertyUsingDotNotation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { prop: 'value' };
                                                   const deleteResult = delete obj.prop;
                                                   deleteResult && obj.prop === undefined;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_RemovesPropertyUsingBracketNotation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { prop: 'value' };
                                                   const deleteResult = delete obj['prop'];
                                                   deleteResult && obj['prop'] === undefined;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_RemovesPropertyWithVariableKey()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { a: 1, b: 2, c: 3 };
                                                   const key = 'b';
                                                   delete obj[key];
                                                   obj.a === 1 && obj.b === undefined && obj.c === 3;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_ReturnsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { prop: 'value' };
                                                   delete obj.prop;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_OnNonExistentProperty_ReturnsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { prop: 'value' };
                                                   delete obj.nonExistent;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Delete_OnArrayElement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const arr = [1, 2, 3, 4, 5];
                                                   delete arr[2];
                                                   arr.length === 5 && arr[2] === undefined;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_OriginalProblemStatement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const DYNAMIC_REQUIRE_CACHE = Object.create(null);
                                                   const resolvedPath = 'test/path';
                                                   DYNAMIC_REQUIRE_CACHE[resolvedPath] = { data: 'test' };
                                                   delete DYNAMIC_REQUIRE_CACHE[resolvedPath];
                                                   DYNAMIC_REQUIRE_CACHE[resolvedPath] === undefined;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_WithNestedPropertyAccess()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { nested: { prop: 'value' } };
                                                   delete obj.nested.prop;
                                                   obj.nested.prop === undefined;
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_AsMethodName_InObjectLiteral()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { 
                                                       delete: function(val) { 
                                                           return 'deleted: ' + val; 
                                                       } 
                                                   };
                                                   obj.delete('test');
                                               
                                           """);
        Assert.Equal("deleted: test", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_AsMethodName_WithDotNotation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { 
                                                       delete: function(val) { 
                                                           return val === 'test'; 
                                                       } 
                                                   };
                                                   obj.delete('test');
                                               
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Delete_MultipleProperties()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                   const obj = { a: 1, b: 2, c: 3, d: 4 };
                                                   delete obj.a;
                                                   delete obj['c'];
                                                   Object.keys(obj).length === 2 && obj.b === 2 && obj.d === 4;
                                               
                                           """);
        Assert.True((bool)result!);
    }
}

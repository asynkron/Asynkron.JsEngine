namespace Asynkron.JsEngine.Tests;

public class WeakSetTests
{
    [Fact(Timeout = 2000)]
    public async Task WeakSet_Constructor_Creates_Empty_WeakSet()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       typeof ws;
                                                   
                                           """);
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Add_Adds_Object()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj = { id: 1 };
                                                       ws.add(obj);
                                                       ws.has(obj);
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Add_Returns_WeakSet_For_Chaining()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj1 = { id: 1 };
                                                       let obj2 = { id: 2 };
                                                       let result = ws.add(obj1).add(obj2);
                                                       typeof result;
                                                   
                                           """);
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Has_Checks_Value_Existence()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj1 = { id: 1 };
                                                       let obj2 = { id: 2 };
                                                       ws.add(obj1);
                                                       let has1 = ws.has(obj1);
                                                       let has2 = ws.has(obj2);
                                                       has1 && !has2;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Delete_Removes_Value()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj = { id: 1 };
                                                       ws.add(obj);
                                                       let deleted = ws.delete(obj);
                                                       let stillExists = ws.has(obj);
                                                       deleted && !stillExists;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Delete_Returns_False_For_Nonexistent_Value()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj = { id: 1 };
                                                       ws.delete(obj);
                                                   
                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Rejects_String_As_Value()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let ws = new WeakSet();
                        ws.add("string");
                    
            """));
        Assert.Contains("Invalid value used in weak set", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Rejects_Number_As_Value()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let ws = new WeakSet();
                        ws.add(42);
                    
            """));
        Assert.Contains("Invalid value used in weak set", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Rejects_Boolean_As_Value()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let ws = new WeakSet();
                        ws.add(true);
                    
            """));
        Assert.Contains("Invalid value used in weak set", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Rejects_Null_As_Value()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let ws = new WeakSet();
                        ws.add(null);
                    
            """));
        Assert.Contains("Invalid value used in weak set", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Rejects_Undefined_As_Value()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let ws = new WeakSet();
                        let x = undefined;
                        ws.add(x);
                    
            """));
        Assert.Contains("Invalid value used in weak set", exception.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Accepts_Array_As_Value()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let arr = [1, 2, 3];
                                                       ws.add(arr);
                                                       ws.has(arr);
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task WeakSet_Accepts_Function_As_Value()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let fn = function() { return 42; };
                                                       ws.add(fn);
                                                       ws.has(fn);
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Different_Objects_Are_Different_Values()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj1 = { x: 1 };
                                                       let obj2 = { x: 1 };
                                                       ws.add(obj1);
                                                       let has1 = ws.has(obj1);
                                                       let has2 = ws.has(obj2);
                                                       has1 && !has2;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Does_Not_Add_Duplicate_Objects()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       let obj = { id: 1 };
                                                       ws.add(obj);
                                                       ws.add(obj);
                                                       ws.add(obj);
                                                       ws.has(obj);
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Has_Returns_False_For_Primitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       ws.has("string");
                                                   
                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Delete_Returns_False_For_Primitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       ws.delete("string");
                                                   
                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Constructor_Accepts_Array_Of_Values()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj1 = { id: 1 };
                                                       let obj2 = { id: 2 };
                                                       let values = [obj1, obj2];
                                                       let ws = new WeakSet(values);
                                                       let has1 = ws.has(obj1);
                                                       let has2 = ws.has(obj2);
                                                       has1 && has2;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Typeof_Returns_Object()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws = new WeakSet();
                                                       typeof ws;
                                                   
                                           """);
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Can_Store_Same_Object_In_Different_WeakSets()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws1 = new WeakSet();
                                                       let ws2 = new WeakSet();
                                                       let obj = { id: 1 };
                                                       ws1.add(obj);
                                                       ws2.add(obj);
                                                       let has1 = ws1.has(obj);
                                                       let has2 = ws2.has(obj);
                                                       has1 && has2;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_Delete_Does_Not_Affect_Other_WeakSets()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let ws1 = new WeakSet();
                                                       let ws2 = new WeakSet();
                                                       let obj = { id: 1 };
                                                       ws1.add(obj);
                                                       ws2.add(obj);
                                                       ws1.delete(obj);
                                                       let has1 = ws1.has(obj);
                                                       let has2 = ws2.has(obj);
                                                       !has1 && has2;
                                                   
                                           """);
        Assert.True((bool)result!);
    }
}
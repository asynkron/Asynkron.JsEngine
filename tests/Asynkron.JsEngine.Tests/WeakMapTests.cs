namespace Asynkron.JsEngine.Tests;

public class WeakMapTests
{
    [Fact]
    public async Task WeakMap_Constructor_Creates_Empty_WeakMap()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       typeof wm;
                                                   
                                           """);
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task WeakMap_Set_And_Get_With_Object_Key()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       wm.set(obj, "value");
                                                       wm.get(obj);
                                                   
                                           """);
        Assert.Equal("value", result);
    }

    [Fact]
    public async Task WeakMap_Set_Returns_WeakMap_For_Chaining()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj1 = { id: 1 };
                                                       let obj2 = { id: 2 };
                                                       let result = wm.set(obj1, "a").set(obj2, "b");
                                                       typeof result;
                                                   
                                           """);
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task WeakMap_Has_Checks_Key_Existence()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj1 = { id: 1 };
                                                       let obj2 = { id: 2 };
                                                       wm.set(obj1, "value");
                                                       let has1 = wm.has(obj1);
                                                       let has2 = wm.has(obj2);
                                                       has1 && !has2;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Delete_Removes_Entry()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       wm.set(obj, "value");
                                                       let deleted = wm.delete(obj);
                                                       let stillExists = wm.has(obj);
                                                       deleted && !stillExists;
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Delete_Returns_False_For_Nonexistent_Key()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       wm.delete(obj);
                                                   
                                           """);
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Get_Returns_Undefined_For_Missing_Key()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       let value = wm.get(obj);
                                                       typeof value;
                                                   
                                           """);
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task WeakMap_Rejects_String_As_Key()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let wm = new WeakMap();
                        wm.set("string", "value");
                    
            """));
        Assert.Contains("Invalid value used as weak map key", exception.Message);
    }

    [Fact]
    public async Task WeakMap_Rejects_Number_As_Key()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let wm = new WeakMap();
                        wm.set(42, "value");
                    
            """));
        Assert.Contains("Invalid value used as weak map key", exception.Message);
    }

    [Fact]
    public async Task WeakMap_Rejects_Boolean_As_Key()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let wm = new WeakMap();
                        wm.set(true, "value");
                    
            """));
        Assert.Contains("Invalid value used as weak map key", exception.Message);
    }

    [Fact]
    public async Task WeakMap_Rejects_Null_As_Key()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let wm = new WeakMap();
                        wm.set(null, "value");
                    
            """));
        Assert.Contains("Invalid value used as weak map key", exception.Message);
    }

    [Fact]
    public async Task WeakMap_Rejects_Undefined_As_Key()
    {
        var engine = new JsEngine();
        var exception = await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate("""

                        let wm = new WeakMap();
                        let x = undefined;
                        wm.set(x, "value");
                    
            """));
        Assert.Contains("Invalid value used as weak map key", exception.Message);
    }

    [Fact]
    public async Task WeakMap_Accepts_Array_As_Key()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let arr = [1, 2, 3];
                                                       wm.set(arr, "array value");
                                                       wm.get(arr);
                                                   
                                           """);
        Assert.Equal("array value", result);
    }

    [Fact]
    public async Task WeakMap_Accepts_Function_As_Key()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let fn = function() { return 42; };
                                                       wm.set(fn, "function value");
                                                       wm.get(fn);
                                                   
                                           """);
        Assert.Equal("function value", result);
    }

    [Fact]
    public async Task WeakMap_Different_Objects_Are_Different_Keys()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj1 = { x: 1 };
                                                       let obj2 = { x: 1 };
                                                       wm.set(obj1, "value1");
                                                       wm.set(obj2, "value2");
                                                       let v1 = wm.get(obj1);
                                                       let v2 = wm.get(obj2);
                                                       v1 === "value1" && v2 === "value2";
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Updates_Existing_Key()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       wm.set(obj, "value1");
                                                       wm.set(obj, "value2");
                                                       wm.get(obj);
                                                   
                                           """);
        Assert.Equal("value2", result);
    }

    [Fact]
    public async Task WeakMap_Can_Store_Undefined_As_Value()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       let undef = undefined;
                                                       wm.set(obj, undef);
                                                       wm.has(obj);
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Can_Store_Null_As_Value()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let obj = { id: 1 };
                                                       wm.set(obj, null);
                                                       wm.get(obj);
                                                   
                                           """);
        Assert.Null(result);
    }

    [Fact]
    public async Task WeakMap_Has_Returns_False_For_Primitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       wm.has("string");
                                                   
                                           """);
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Get_Returns_Undefined_For_Primitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       let value = wm.get("string");
                                                       typeof value;
                                                   
                                           """);
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task WeakMap_Delete_Returns_False_For_Primitive()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       wm.delete("string");
                                                   
                                           """);
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Constructor_Accepts_Array_Of_Entries()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj1 = { id: 1 };
                                                       let obj2 = { id: 2 };
                                                       let entries = [[obj1, "a"], [obj2, "b"]];
                                                       let wm = new WeakMap(entries);
                                                       let v1 = wm.get(obj1);
                                                       let v2 = wm.get(obj2);
                                                       v1 === "a" && v2 === "b";
                                                   
                                           """);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task WeakMap_Typeof_Returns_Object()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let wm = new WeakMap();
                                                       typeof wm;
                                                   
                                           """);
        Assert.Equal("object", result);
    }
}

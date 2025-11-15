namespace Asynkron.JsEngine.Tests;

public class SetTests
{
    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.

    [Fact(Timeout = 2000)]
    public async Task Set_Constructor_Creates_Empty_Set()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.size;

                                           """);
        Assert.Equal(0.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Add_Adds_Value()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add("value");
                                                       mySet.has("value");

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Add_Returns_Set_For_Chaining()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1).add(2).add(3);
                                                       mySet.size;

                                           """);
        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Has_Checks_Value_Existence()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add("value");
                                                       let has1 = mySet.has("value");
                                                       let has2 = mySet.has("missing");
                                                       has1 && !has2;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Delete_Removes_Value()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add("value");
                                                       let deleted = mySet.delete("value");
                                                       let stillExists = mySet.has("value");
                                                       deleted && !stillExists;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Delete_Returns_False_For_Nonexistent_Value()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.delete("missing");

                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Clear_Removes_All_Values()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1);
                                                       mySet.add(2);
                                                       mySet.clear();
                                                       mySet.size;

                                           """);
        Assert.Equal(0.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Size_Tracks_Value_Count()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       let s1 = mySet.size;
                                                       mySet.add(1);
                                                       let s2 = mySet.size;
                                                       mySet.add(2);
                                                       let s3 = mySet.size;
                                                       mySet.delete(1);
                                                       let s4 = mySet.size;
                                                       s1 === 0 && s2 === 1 && s3 === 2 && s4 === 1;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Only_Stores_Unique_Values()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1);
                                                       mySet.add(1);
                                                       mySet.add(1);
                                                       mySet.size;

                                           """);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Accepts_Any_Type_As_Value()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       let obj = { id: 1 };
                                                       mySet.add(obj);
                                                       mySet.add(42);
                                                       mySet.add("string");
                                                       mySet.add(true);

                                                       let h1 = mySet.has(obj);
                                                       let h2 = mySet.has(42);
                                                       let h3 = mySet.has("string");
                                                       let h4 = mySet.has(true);

                                                       h1 && h2 && h3 && h4;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_ForEach_Iterates_All_Values()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1);
                                                       mySet.add(2);
                                                       mySet.add(3);

                                                       let sum = 0;
                                                       mySet.forEach(function(value1, value2, s) {
                                                           sum = sum + value1;
                                                       });
                                                       sum;

                                           """);
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Values_Returns_Array_Of_Values()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1);
                                                       mySet.add(2);
                                                       mySet.add(3);

                                                       let values = mySet.values();
                                                       values[0] + values[1] + values[2];

                                           """);
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Keys_Returns_Array_Of_Values()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1);
                                                       mySet.add(2);

                                                       let keys = mySet.keys();
                                                       keys[0] + keys[1];

                                           """);
        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Entries_Returns_Array_Of_Value_Value_Pairs()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add(1);
                                                       mySet.add(2);

                                                       let entries = mySet.entries();
                                                       let first = entries[0];
                                                       first[0] === first[1] && first[0] === 1;

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Maintains_Insertion_Order()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       mySet.add("third");
                                                       mySet.add("first");
                                                       mySet.add("second");

                                                       let values = mySet.values();
                                                       values[0] + "," + values[1] + "," + values[2];

                                           """);
        Assert.Equal("third,first,second", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Constructor_Accepts_Array_Of_Values()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let values = [1, 2, 3, 2, 1];
                                                       let mySet = new Set(values);
                                                       mySet.size;

                                           """);
        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Handles_NaN_As_Value()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       let nan = 0 / 0;
                                                       mySet.add(nan);
                                                       mySet.has(nan);

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Multiple_NaN_Values_Are_Considered_Same()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       let nan1 = 0 / 0;
                                                       let nan2 = 0 / 0;
                                                       mySet.add(nan1);
                                                       mySet.add(nan2);
                                                       mySet.size;

                                           """);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Typeof_Returns_Object()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       typeof mySet;

                                           """);
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Can_Store_Objects()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       let obj1 = { name: "Alice" };
                                                       let obj2 = { name: "Bob" };
                                                       mySet.add(obj1);
                                                       mySet.add(obj2);
                                                       mySet.add(obj1); // duplicate, should not increase size

                                                       mySet.size;

                                           """);
        Assert.Equal(2.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Different_Objects_With_Same_Content_Are_Different()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let mySet = new Set();
                                                       let obj1 = { x: 1 };
                                                       let obj2 = { x: 1 };
                                                       mySet.add(obj1);
                                                       mySet.add(obj2);
                                                       mySet.size;

                                           """);
        Assert.Equal(2.0, result);
    }
}

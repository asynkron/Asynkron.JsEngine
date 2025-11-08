namespace Asynkron.JsEngine.Tests;

public class SetTests
{
    [Fact]
    public void Set_Constructor_Creates_Empty_Set()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.size;
        ");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Set_Add_Adds_Value()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(""value"");
            set.has(""value"");
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_Add_Returns_Set_For_Chaining()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1).add(2).add(3);
            set.size;
        ");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Set_Has_Checks_Value_Existence()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(""value"");
            let has1 = set.has(""value"");
            let has2 = set.has(""missing"");
            has1 && !has2;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_Delete_Removes_Value()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(""value"");
            let deleted = set.delete(""value"");
            let stillExists = set.has(""value"");
            deleted && !stillExists;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_Delete_Returns_False_For_Nonexistent_Value()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.delete(""missing"");
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public void Set_Clear_Removes_All_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1);
            set.add(2);
            set.clear();
            set.size;
        ");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Set_Size_Tracks_Value_Count()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            let s1 = set.size;
            set.add(1);
            let s2 = set.size;
            set.add(2);
            let s3 = set.size;
            set.delete(1);
            let s4 = set.size;
            s1 === 0 && s2 === 1 && s3 === 2 && s4 === 1;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_Only_Stores_Unique_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1);
            set.add(1);
            set.add(1);
            set.size;
        ");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Set_Accepts_Any_Type_As_Value()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            let obj = { id: 1 };
            set.add(obj);
            set.add(42);
            set.add(""string"");
            set.add(true);
            
            let h1 = set.has(obj);
            let h2 = set.has(42);
            let h3 = set.has(""string"");
            let h4 = set.has(true);
            
            h1 && h2 && h3 && h4;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_ForEach_Iterates_All_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1);
            set.add(2);
            set.add(3);
            
            let sum = 0;
            set.forEach(function(value1, value2, s) {
                sum = sum + value1;
            });
            sum;
        ");
        Assert.Equal(6.0, result);
    }

    [Fact]
    public void Set_Values_Returns_Array_Of_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1);
            set.add(2);
            set.add(3);
            
            let values = set.values();
            values[0] + values[1] + values[2];
        ");
        Assert.Equal(6.0, result);
    }

    [Fact]
    public void Set_Keys_Returns_Array_Of_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1);
            set.add(2);
            
            let keys = set.keys();
            keys[0] + keys[1];
        ");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Set_Entries_Returns_Array_Of_Value_Value_Pairs()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(1);
            set.add(2);
            
            let entries = set.entries();
            let first = entries[0];
            first[0] === first[1] && first[0] === 1;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_Maintains_Insertion_Order()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            set.add(""third"");
            set.add(""first"");
            set.add(""second"");
            
            let values = set.values();
            values[0] + "","" + values[1] + "","" + values[2];
        ");
        Assert.Equal("third,first,second", result);
    }

    [Fact]
    public void Set_Constructor_Accepts_Array_Of_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let values = [1, 2, 3, 2, 1];
            let set = new Set(values);
            set.size;
        ");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Set_Handles_NaN_As_Value()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            let nan = 0 / 0;
            set.add(nan);
            set.has(nan);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Set_Multiple_NaN_Values_Are_Considered_Same()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            let nan1 = 0 / 0;
            let nan2 = 0 / 0;
            set.add(nan1);
            set.add(nan2);
            set.size;
        ");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Set_Typeof_Returns_Object()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            typeof set;
        ");
        Assert.Equal("object", result);
    }

    [Fact]
    public void Set_Can_Store_Objects()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            let obj1 = { name: ""Alice"" };
            let obj2 = { name: ""Bob"" };
            set.add(obj1);
            set.add(obj2);
            set.add(obj1); // duplicate, should not increase size
            
            set.size;
        ");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Set_Different_Objects_With_Same_Content_Are_Different()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let set = new Set();
            let obj1 = { x: 1 };
            let obj2 = { x: 1 };
            set.add(obj1);
            set.add(obj2);
            set.size;
        ");
        Assert.Equal(2.0, result);
    }
}

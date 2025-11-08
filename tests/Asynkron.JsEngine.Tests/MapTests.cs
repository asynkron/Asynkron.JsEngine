namespace Asynkron.JsEngine.Tests;

public class MapTests
{
    [Fact]
    public void Map_Methods_Are_Functions()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            typeof map.set;
        ");
        Assert.Equal("function", result);
    }

    [Fact]
    public void Map_Constructor_Creates_Empty_Map()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.size;
        ");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Map_Set_And_Get()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""key1"", ""value1"");
            map.get(""key1"");
        ");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void Map_Set_Returns_Map_For_Chaining()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""a"", 1).set(""b"", 2).set(""c"", 3);
            map.size;
        ");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Map_Has_Checks_Key_Existence()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""key"", ""value"");
            let has1 = map.has(""key"");
            let has2 = map.has(""missing"");
            has1 && !has2;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Map_Delete_Removes_Entry()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""key"", ""value"");
            let deleted = map.delete(""key"");
            let stillExists = map.has(""key"");
            deleted && !stillExists;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Map_Delete_Returns_False_For_Nonexistent_Key()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.delete(""missing"");
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public void Map_Clear_Removes_All_Entries()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""a"", 1);
            map.set(""b"", 2);
            map.clear();
            map.size;
        ");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Map_Size_Tracks_Entry_Count()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            let s1 = map.size;
            map.set(""a"", 1);
            let s2 = map.size;
            map.set(""b"", 2);
            let s3 = map.size;
            map.delete(""a"");
            let s4 = map.size;
            s1 === 0 && s2 === 1 && s3 === 2 && s4 === 1;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Map_Accepts_Any_Type_As_Key()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            let obj = { id: 1 };
            map.set(obj, ""object key"");
            map.set(42, ""number key"");
            map.set(true, ""boolean key"");
            
            let v1 = map.get(obj);
            let v2 = map.get(42);
            let v3 = map.get(true);
            
            v1 === ""object key"" && v2 === ""number key"" && v3 === ""boolean key"";
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Map_ForEach_Iterates_All_Entries()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""a"", 1);
            map.set(""b"", 2);
            map.set(""c"", 3);
            
            let sum = 0;
            map.forEach(function(value, key, m) {
                sum = sum + value;
            });
            sum;
        ");
        Assert.Equal(6.0, result);
    }

    [Fact]
    public void Map_Keys_Returns_Array_Of_Keys()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""a"", 1);
            map.set(""b"", 2);
            
            let keys = map.keys();
            keys[0] + keys[1];
        ");
        Assert.Equal("ab", result);
    }

    [Fact]
    public void Map_Values_Returns_Array_Of_Values()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""a"", 1);
            map.set(""b"", 2);
            
            let values = map.values();
            values[0] + values[1];
        ");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Map_Entries_Returns_Array_Of_Pairs()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""a"", 1);
            map.set(""b"", 2);
            
            let entries = map.entries();
            let first = entries[0];
            first[0] + first[1];
        ");
        Assert.Equal("a1", result);
    }

    [Fact]
    public void Map_Maintains_Insertion_Order()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""third"", 3);
            map.set(""first"", 1);
            map.set(""second"", 2);
            
            let keys = map.keys();
            keys[0] + "","" + keys[1] + "","" + keys[2];
        ");
        Assert.Equal("third,first,second", result);
    }

    [Fact]
    public void Map_Overwrites_Existing_Key()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            map.set(""key"", ""value1"");
            map.set(""key"", ""value2"");
            
            let size = map.size;
            let value = map.get(""key"");
            
            size === 1 && value === ""value2"";
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Map_Constructor_Accepts_Array_Of_Entries()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let entries = [[""a"", 1], [""b"", 2], [""c"", 3]];
            let map = new Map(entries);
            let size = map.size;
            let value = map.get(""b"");
            size === 3 && value === 2;
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Map_Get_Returns_Undefined_For_Missing_Key()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            let value = map.get(""missing"");
            typeof value;
        ");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public void Map_Handles_NaN_As_Key()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            let nan = 0 / 0;
            map.set(nan, ""NaN value"");
            map.get(nan);
        ");
        Assert.Equal("NaN value", result);
    }

    [Fact]
    public void Map_Typeof_Returns_Object()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let map = new Map();
            typeof map;
        ");
        Assert.Equal("object", result);
    }
}

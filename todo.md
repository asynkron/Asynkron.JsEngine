# TODO

## Migrate HostFunction Creations to JsHostFunction Annotations
Identify helper methods that manually create HostFunction instances and
wire up names/length/prototype. These should be migrated to generator‑annotated
host functions. Search in *Helper.cs for patterns like public static HostFunction Create...
and new HostFunction(...), then convert to a [JsHostFunction] method that returns JsValue directly.

Before
```csharp
public static HostFunction CreateParseIntFunction()
{
    //define lambda manuallt and return the HostFunction
    var fn = new HostFunction(args =>
    {
        // logic...
        return new JsValue(result);
    }, isConstructor: false);
    fn.Properties.Delete("prototype");
    return fn;
}
```

After, wired by source generators, looks almost like a normal method:
```csharp
[JsHostFunction("parseInt", Length = 2d, DeletePrototype = true)]
private static JsValue ParseInt(IReadOnlyList<JsValue> args)
{
    // same logic...
    return new JsValue(result);
}
```

Your task is to find the next such method to convert.
You do this by listing all *Helper.cs files in alphabetical order,
Then you do the same with all methods in each file. then you simply check, is this one done? yes?, the you continue to the next.
eventually you will find one that is not yet converted.
Fix that one, and go back to sleep.


You can verify correctness by running existing unit tests (not the 262 kit)
to ensure no regressions occur after the migration.

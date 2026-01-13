# TODO

## Migrate HostFunction Creations to JsHostFunction Annotations
Identify helper methods that manually create HostFunction instances and wire up names/length/prototype. These should be migrated to generator‑annotated host functions. Search in *Helper.cs for patterns like public static HostFunction Create... and new HostFunction(...), then convert to a [JsHostFunction] method that returns JsValue directly.
Before
```csharp
public static HostFunction CreateParseIntFunction()
{
    var fn = new HostFunction(args =>
    {
        // logic...
        return new JsValue(result);
    }, isConstructor: false);
    fn.Properties.Delete("prototype");
    return fn;
}
```

After
```csharp
[JsHostFunction("parseInt", Length = 2d, DeletePrototype = true)]
private static JsValue ParseInt(IReadOnlyList<JsValue> args)
{
    // same logic...
    return new JsValue(result);
}
```

Success criteria is when you have converted at least 5 such functions in a *Helper.cs file to use the [JsHostFunction] annotation instead of manual HostFunction creation. the more the better.

You can verify correctness by running existing unit tests (not the 262 kit) to ensure no regressions occur after the migration.

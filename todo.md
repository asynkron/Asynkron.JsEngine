# TODO

## Migrate Manual HostFunction Creations to Source-Generated Attributes

Manual `HostFunction` instantiation in `*Helper.cs` files should be migrated to the attribute-based source generator approach. This reduces boilerplate, improves consistency, and leverages compile-time code generation.

### Available Source Generator Attributes

Located in `src/Asynkron.JsEngine/Runtime/Prototypes/`:

| Attribute | Use Case |
|-----------|----------|
| `JsHostFunctionAttribute` | Global functions (e.g., `parseInt`, `encodeURI`). Set `Target` property for constructor/prototype placement. |
| `JsHostMethodAttribute` | Prototype methods (e.g., `Array.prototype.push`) |
| `JsHostGetterAttribute` | Property getters on prototypes |
| `JsHostSetterAttribute` | Property setters on prototypes |
| `JsConstructorAttribute` | Class-level attribute marking a constructor function |
| `JsConstructorMethodAttribute` | Static methods on constructors (e.g., `Object.keys`, `Array.isArray`) |
| `JsSymbolMethodAttribute` | Symbol-keyed methods (e.g., `[Symbol.iterator]`) |
| `JsSymbolGetterAttribute` | Symbol-keyed getters (e.g., `[Symbol.toStringTag]`) |
| `JsPrototypeAttribute` | Class-level attribute marking a prototype class |
| `JsMethodAliasAttribute` | Alias one method to another (e.g., `toGMTString` -> `toUTCString`) |

### Migration Pattern

**Before** (manual instantiation):
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

**After** (source-generated):
```csharp
[JsHostFunction("parseInt", Length = 2d, DeletePrototype = true)]
private static JsValue ParseInt(IReadOnlyList<JsValue> args)
{
    // same logic...
    return new JsValue(result);
}
```

### Key Points

1. The class must be `partial` for the source generator to emit code
2. Methods should be `private static` returning `JsValue`
3. Signature options: `(IReadOnlyList<JsValue> args)`, `(IReadOnlyList<JsValue> args, RealmState realm)`, or `(IReadOnlyList<JsValue> args, EvaluationContext? context)`
4. Old factory methods can be marked `[Obsolete]` during transition

### Example Migration

See commit [`c38fc206`](https://github.com/AsynkronIT/Asynkron.JsEngine/commit/c38fc206) for a complete example migrating `Intl.getCanonicalLocales` from manual HostFunction to `[JsHostFunction]` attribute.

### How to Find Work

1. Search `*Helper.cs` files in `src/Asynkron.JsEngine/StdLib/` for `new HostFunction`
2. Process files alphabetically, methods alphabetically within each file
3. Skip methods that capture closure state (iterator index, etc.) - these cannot be source-generated
4. Convert one method, run tests, commit, repeat

### Verification

Run internal unit tests (not ECMAScript 262 tests) to verify no regressions:
```bash
dotnet test tests/Asynkron.JsEngine.Tests
```

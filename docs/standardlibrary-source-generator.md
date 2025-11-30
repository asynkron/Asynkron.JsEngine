# Source generator feasibility for standard library bindings

## Why consider a generator?
Standard library modules currently wire C# methods onto `JsObject` instances by repetitively calling helper extensions like `SetHostedProperty`, passing manual adapters that unpack the `args` array. For example, `StandardLibrary.Number` uses `SetHostedProperty` to attach multiple methods to the number wrapper and constructor objects, duplicating binding code across many files.【F:src/Asynkron.JsEngine/StdLib/StandardLibrary.Number.cs†L40-L51】【F:src/Asynkron.JsEngine/StdLib/StandardLibrary.Number.cs†L470-L480】 A source generator could reduce this repetition by deriving bindings from annotated classes and methods.

## Feasible approach
- **Attribute-driven model**: Introduce attributes (e.g., `[JsBuiltin("toString")]`, `[JsConstructor]`) on partial C# classes that represent built-ins. The generator would emit partials that register annotated members with `JsObject` instances during standard library initialization, replacing many direct `SetHostedProperty` calls.
- **Argument conversion helpers**: The generator can emit marshaling code that converts `IReadOnlyList<object?> args` into strongly-typed parameters (numbers, strings, `JsObject`), handling `undefined`/`null` and `RealmState` where needed. Overloads or optional parameters could map to ECMAScript defaults.
- **Lifecycle hooks**: Support annotations for prototype methods vs. constructor statics, and allow per-method flags (needs `realm`, uses `this`, `Length` metadata) so the generator can produce wrappers that respect the runtime expectations already encoded manually.

## Constraints and risks
- **Spec fidelity**: Many existing bindings implement subtle ECMAScript semantics (length clamping, iterator closing, proxy observable `delete`/`has` order). Generated wrappers would still need explicit hooks for these behaviors; the generator cannot infer them without annotations, so authors must supply flags or custom bodies.
- **Performance and allocation**: Generated adapters should avoid closures that capture large contexts to match current patterns. The design must still avoid prohibited shared-state constructs and blocking calls, preserving the existing async-friendly architecture.
- **Discoverability vs. clarity trade-off**: While a generator reduces boilerplate, it introduces an additional build step and implicit behavior. Clear documentation and attribute naming are needed so maintainers can trace how a JS-visible method is bound.

## Suggested experiment
A practical first step is to prototype a generator for one module (e.g., `StandardLibrary.Number`) using attributes to map methods and required realm/context parameters. Measure whether the generated binding code matches the current manual `SetHostedProperty` wiring and preserves behavior in existing tests. If successful, the pattern can expand to other standard library files to cut down on repetitive binding code.

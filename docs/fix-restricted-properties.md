# Fix Function.prototype Restricted Properties

## Problem Summary
`Function.prototype.caller` and `Function.prototype.arguments` should throw `TypeError` when accessed on class constructors and generator functions, but the engine is not properly finding and invoking the setter through the prototype chain.

## Hard Facts

- `Function.prototype.caller` and `Function.prototype.arguments` are accessor properties with getter/setter that throw `TypeError`
- Prototype chain for class constructors: `SubClass.__proto__ === BaseClass`, `BaseClass.__proto__ === Function.prototype`
- `JsObject.SetProperty` must traverse the prototype chain to find setters on non-`JsObject` prototypes
- `TypedFunction` instances store their prototype as `IJsObjectLike`, not always `JsObject`
- `TypedGeneratorFactory` instances created via `function* g() {}` syntax must implement `ICallerInfo` with `IsStrictFunction => true`
- Generator functions created via `new GeneratorFunction()` constructor currently return `TypedFunction` instead of `TypedGeneratorFactory`

## Test Status

- **13/15 restricted-properties tests pass**
- **2 tests fail**: `GeneratorFunction/instance-restricted-properties.js` (strict and non-strict)
- Syntax-based generators work: `syntaxGen.caller` throws `TypeError`
- Constructor-based generators don't work: `new GeneratorFunction().caller` returns `undefined`

## What We Have Tried

- Added `FindSetterInPrototypeChain` method to `JsObject.cs` to traverse non-`JsObject` prototypes
- Added `ICallerInfo` implementation to `TypedGeneratorFactory`:
  - Added `_caller` field
  - Implemented `ICallerInfo.Caller` property
  - Set `ICallerInfo.IsStrictFunction => true` (generators are always strict)
- Verified `StandardLibrary.Function.cs` has proper getter logic checking `ICallerInfo.IsStrictFunction`

## Why It's Still Failing

- `GeneratorFunction` constructor (accessible via `Object.getPrototypeOf(function*() {}).constructor`) uses the same code path as `Function` constructor
- When you call `new GeneratorFunction()`, it parses the source and creates a `TypedFunction`, not a `TypedGeneratorFactory`
- The engine doesn't have a distinct `GeneratorFunction` constructor implementation

## What We Should Try Next

- Implement a proper `GeneratorFunction` constructor in `JsEngine.cs` that:
  - Parses the body as a generator function
  - Returns a `TypedGeneratorFactory` instance (not `TypedFunction`)
  - Sets up the proper prototype chain (`GeneratorFunction.prototype`)
- Find where `GeneratorFunction` is exposed (likely in `EnsureGeneratorIntrinsics` or `JsEngine.cs`)
- Modify the constructor callable to detect when creating a generator function and route to appropriate factory

## Key Files

- `src/Asynkron.JsEngine/JsTypes/JsObject.cs` - `FindSetterInPrototypeChain`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorFactory.cs` - `ICallerInfo` implementation
- `src/Asynkron.JsEngine/StdLib/StandardLibrary.Function.cs` - restricted property getter/setter
- `src/Asynkron.JsEngine/JsEngine.cs` - likely where `GeneratorFunction` constructor is registered

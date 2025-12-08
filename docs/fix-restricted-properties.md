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
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorFactory.cs` - `ICallerInfo` implementation, `EnsureGeneratorIntrinsics()`
- `src/Asynkron.JsEngine/StdLib/StandardLibrary.Function.cs` - restricted property getter/setter, `FunctionConstructorBody()`
- `src/Asynkron.JsEngine/JsEngine.cs` - global object setup

## New Findings (December 8, 2025)

### Root Cause Identified

1. `GeneratorFunctionPrototype` is created in `EnsureGeneratorIntrinsics()` but **has no `constructor` property**
2. When JS accesses `Object.getPrototypeOf(function*() {}).constructor`, it falls back to `Function.prototype.constructor` (the regular `Function`)
3. `StandardLibrary.Function.cs:FunctionConstructorBody()` at line 224:
   - Parses source as `(function anonymous(...) { ... })`
   - Executes via `engine.ExecuteProgram()` → returns `TypedFunction`, not `TypedGeneratorFactory`
   - There's no detection of whether the caller was `GeneratorFunction` vs `Function`

### Solution Path

1. Create a `GeneratorFunction` constructor (a `HostFunction`) with its own body that:
   - Builds source as `(function* anonymous(...) { ... })` (note the `*`)
   - Parses and executes → this will create a `TypedGeneratorFactory`
2. Set `GeneratorFunctionPrototype.constructor` = the new `GeneratorFunction` constructor
3. This constructor must be created **lazily** when `EnsureGeneratorIntrinsics()` is called

### Implementation Location

The best place to add this is in `EnsureGeneratorIntrinsics()` in `TypedAstEvaluator.TypedGeneratorFactory.cs`:
- Create a `GeneratorFunction` constructor `HostFunction`
- Parse body as generator function: `(function* anonymous({paramList}) {{ {bodySource} }})`
- Set `GeneratorFunctionPrototype["constructor"]` = the new constructor
- Ensure prototype chain: `GeneratorFunction.__proto__ === Function`

## Fix Applied (December 8, 2025)

**All 15 restricted-properties tests now pass!**

### Changes Made

1. **`src/Asynkron.JsEngine/Runtime/RealmState.cs`**:
   - Added `GeneratorFunctionConstructor` property to hold the constructor reference

2. **`src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedGeneratorFactory.cs`**:
   - Modified `EnsureGeneratorIntrinsics()` to create the `GeneratorFunction` constructor:
     - Uses `_realmState.Engine` to parse/execute generator function source
     - Sets `GeneratorFunctionPrototype.constructor = GeneratorFunction`
     - Sets `GeneratorFunction.__proto__ = Function.prototype`
     - Sets `GeneratorFunction.prototype = GeneratorFunctionPrototype`
   - Added `CreateGeneratorFunctionConstructor()` method to create the constructor `HostFunction`
   - Added `GeneratorFunctionConstructorBody()` method that:
     - Builds source as `(function* anonymous({params}) { {body} })`
     - Parses and executes → creates `TypedGeneratorFactory`
   - Added `ToFunctionArgumentString()` helper (same logic as in `StandardLibrary.Function.cs`)

### Key Insight

The fix ensures that when `new GeneratorFunction()` is called:
1. The source is parsed as `function* anonymous(...)` (note the `*`)
2. Execution returns a `TypedGeneratorFactory` (not `TypedFunction`)
3. `TypedGeneratorFactory` implements `ICallerInfo` with `IsStrictFunction => true`
4. Accessing `.caller` or `.arguments` now correctly throws `TypeError`

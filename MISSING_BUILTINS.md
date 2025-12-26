# Missing Builtin Methods Status

This document tracks the implementation status of JavaScript builtins from `todo-builtins.md`.

## Summary

- **Total methods in todo-builtins.md**: 816
- **Existing implementations marked with /* FLAKY */**: 318
- **Stub methods created for existing types**: 33
- **New prototype classes created**: 21 classes with 139 methods
- **Total scaffolded/marked**: 490 methods (60% of all methods)
- **Remaining methods**: 326 methods requiring implementation or new classes

## Completed Work

### ✅ FLAKY Comments Added (318 methods)
All existing method implementations now have `/* FLAKY */` comments indicating potential bugs.

### ✅ Stub Methods Created for Existing Types (33 methods)
DataView (6), Promise (1), Date (1), Map (1), Math (1), Object (1), Set (7), String (4), Symbol (2), TypedArray (1)

### ✅ New Prototype Classes Created (21 classes, 139 methods)

#### Resource Management & Modern Features (4 classes, 17 methods)
1. **FinalizationRegistry** (3) - Finalization callbacks
2. **DisposableStack** (6) - Sync resource management
3. **AsyncDisposableStack** (6) - Async resource management
4. **ShadowRealm** (2) - Isolated execution contexts

#### Concurrency (1 class, 13 methods)
5. **Atomics** (13) - Atomic operations on SharedArrayBuffer

#### Iteration (5 classes, 20 methods)
6. **Iterator** (12) - Iterator helpers
7. **AsyncIteratorPrototype** (0) - Base for async iterators
8. **ArrayIteratorPrototype** (1) - Array iteration
9. **GeneratorPrototype** (3) - Generator control
10. **AsyncGeneratorPrototype** (3) - Async generator control

#### Function Constructors (3 classes)
11. **AsyncFunctionConstructor** - Dynamic async function creation
12. **GeneratorFunctionConstructor** - Dynamic generator creation
13. **AsyncGeneratorFunctionConstructor** - Dynamic async generator creation

#### Temporal API (8 types, 90 methods)
14. **Temporal.Instant** (11) - Fixed point in time
15. **Temporal.PlainDate** (10) - Calendar date
16. **Temporal.Duration** (13) - Duration of time
17. **Temporal.PlainTime** (12) - Wall-clock time
18. **Temporal.PlainDateTime** (14) - Date and time
19. **Temporal.ZonedDateTime** (16) - Date/time with timezone
20. **Temporal.PlainYearMonth** (9) - Year and month
21. **Temporal.PlainMonthDay** (5) - Month and day

## Remaining Work - Methods Requiring Implementation (326 methods)

These require creating entirely new classes and significant architectural work:

### Temporal API (251 methods)
Complete implementation of TC39 Temporal proposal:
- `Temporal_Duration` (27 methods)
- `Temporal_Instant` (17 methods)
- `Temporal_Now` (6 methods)
- `Temporal_PlainDate` (36 methods)
- `Temporal_PlainDateTime` (38 methods)
- `Temporal_PlainMonthDay` (9 methods)
- `Temporal_PlainTime` (17 methods)
- `Temporal_PlainYearMonth` (15 methods)
- `Temporal_ZonedDateTime` (52 methods)

### TypedArrayConstructors (51 methods)
Individual constructors and their specific behaviors:
- BigInt64Array, BigUint64Array, Float32Array, Float64Array
- Int8Array, Int16Array, Int32Array
- Uint8Array, Uint8ClampedArray, Uint16Array, Uint32Array
- Constructor variants (bufferArg, lengthArg, noArgs, objectArg, typedarrayArg)
- Internal methods (DefineOwnProperty, Delete, Get, GetOwnProperty, etc.)

### Atomics (26 methods)
SharedArrayBuffer atomic operations:
- `Atomics_add`, `Atomics_and`, `Atomics_compareExchange`, `Atomics_exchange`
- `Atomics_isLockFree`, `Atomics_load`, `Atomics_notify`, `Atomics_or`
- `Atomics_store`, `Atomics_sub`, `Atomics_wait`, `Atomics_waitAsync`, `Atomics_xor`
- Each with regular and `_bigint` variants

### Iterator Protocol (18 methods)
Iterator helpers proposal:
- `Iterator_concat`, `Iterator_from`
- `Iterator_prototype_drop`, `Iterator_prototype_every`, `Iterator_prototype_filter`
- `Iterator_prototype_find`, `Iterator_prototype_flatMap`, `Iterator_prototype_forEach`
- `Iterator_prototype_map`, `Iterator_prototype_reduce`, `Iterator_prototype_some`
- `Iterator_prototype_take`, `Iterator_prototype_toArray`
- Symbol methods: `Symbol_dispose`, `Symbol_iterator`, `Symbol_toStringTag`

### Explicit Resource Management (14 methods)
- `DisposableStack` and `DisposableStack_prototype` (7 methods)
- `AsyncDisposableStack` and `AsyncDisposableStack_prototype` (7 methods)

### NativeErrors (16 methods)
Extended error types:
- `NativeErrors_AggregateError` and `_prototype`
- `NativeErrors_EvalError` and `_prototype`
- `NativeErrors_RangeError` and `_prototype`
- `NativeErrors_ReferenceError` and `_prototype`
- `NativeErrors_SuppressedError` and `_prototype`
- `NativeErrors_SyntaxError` and `_prototype`
- `NativeErrors_TypeError` and `_prototype`
- `NativeErrors_URIError` and `_prototype`

### Other Types Needing New Classes
- `FinalizationRegistry` (3 methods)
- `ShadowRealm` (4 methods)
- `AsyncGeneratorPrototype` (3 methods)
- `AsyncIteratorPrototype` (2 methods)
- `ArrayIteratorPrototype` (2 methods)
- Plus others

## Implementation Strategy

1. **Short term**: Complete stub methods for the 26 remaining methods in existing types
2. **Medium term**: Implement high-priority new types (Iterator, Symbols for resource management)
3. **Long term**: Implement comprehensive APIs (Temporal, full Atomics support)

## Notes

- All stub methods throw `NotImplementedException` with TODO comments
- Each stub is marked with `/* FLAKY */` comment
- Proper method signatures and attributes are in place for code generation
- Implementation can be done incrementally as features are prioritized

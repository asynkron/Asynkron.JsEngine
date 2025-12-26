# Missing Builtin Methods Status

This document tracks the implementation status of JavaScript builtins from `todo-builtins.md`.

## Summary

- **Total methods in todo-builtins.md**: 816
- **Existing implementations marked with /* FLAKY */**: 318
- **Stub methods created for existing types**: 33
- **New prototype classes created**: 13 classes with 49 methods
- **Total scaffolded/marked**: 400 methods (49% of all methods)
- **Remaining methods**: 416 methods requiring implementation or new classes

## Completed Work

### ✅ FLAKY Comments Added (318 methods)
All existing method implementations now have `/* FLAKY */` comments indicating potential bugs:
- JsHostMethod, JsConstructorMethod, JsConstructorSymbolGetter, JsHostGetter attributes
- Covers all major types: Array, Object, String, Number, Date, Promise, Map/Set, TypedArray, RegExp, Proxy, Reflect, Symbol, Math, BigInt, Error, Function, JSON, Console, DataView, ArrayBuffer, WeakMap/WeakSet, WeakRef, SharedArrayBuffer, Boolean, and all Intl variants

### ✅ Stub Methods Created for Existing Types (33 methods)
- **DataView** (6): getBigInt64, getBigUint64, getFloat16, setBigInt64, setBigUint64, setFloat16
- **Promise** (1): withResolvers (ES2024)
- **Date** (1): toTemporalInstant
- **Map** (1): groupBy (ES2024)
- **Math** (1): f16round
- **Object** (1): groupBy (ES2024)
- **Set** (7): difference, intersection, isDisjointFrom, isSubsetOf, isSupersetOf, symmetricDifference, union
- **String** (4): isWellFormed, toWellFormed, toLocaleLowerCase, toLocaleUpperCase
- **Symbol** (2): asyncDispose, dispose
- **TypedArray** (1): subarray

### ✅ New Prototype Classes Created (13 classes, 49 methods)

#### 1. FinalizationRegistry (3 methods)
- register, unregister methods + constructor
- Enables WeakRef-based finalization callbacks

#### 2. Atomics (13 methods)
- add, and, or, xor, sub, compareExchange, exchange, load, store, isLockFree, wait, waitAsync, notify
- Atomic operations on SharedArrayBuffer

#### 3. DisposableStack (6 methods)
- use, adopt, defer, move, dispose + disposed getter
- Synchronous explicit resource management

#### 4. AsyncDisposableStack (6 methods)
- use, adopt, defer, move, disposeAsync + disposed getter
- Asynchronous explicit resource management

#### 5. Iterator (12 methods)
- map, filter, take, drop, flatMap, reduce, toArray, forEach, some, every, find + from()
- Iterator helpers proposal

#### 6. ShadowRealm (2 methods)
- evaluate, importValue
- Isolated execution contexts

#### 7. AsyncIteratorPrototype
- Base prototype for async iterators
- Uses Symbol.asyncIterator

#### 8. ArrayIteratorPrototype (1 method)
- next() method
- For array iterator objects

#### 9. GeneratorPrototype (3 methods)
- next, return, throw
- For generator instances

#### 10. AsyncGeneratorPrototype (3 methods)
- next, return, throw
- For async generator instances

#### 11. AsyncFunctionConstructor
- Constructor for creating async functions dynamically

#### 12. GeneratorFunctionConstructor
- Constructor for creating generator functions dynamically

#### 13. AsyncGeneratorFunctionConstructor
- Constructor for creating async generator functions dynamically

## Remaining Work - Methods Requiring Implementation (416 methods)

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

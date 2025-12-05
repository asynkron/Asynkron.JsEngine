# Language Suite Next Steps

## Current State
- Array built-ins split into focused partials and `%Array.prototype%` is generator-backed; build stayed green with that layout.
- Eval direct/indirect handling is spec-aligned again (including Annex B quirks).
- TypedArray constructors honor new-target for prototype resolution, set `name`, and `TypedArray.from` uses the calling constructor for targets. Resizable length logic goes through `ComputeLength` and iterator/enumeration flows respect detached/out-of-bounds views.
- Derived typed array constructors were returning `undefined`; fixed by routing host super calls through `InvokeWithContext` (new-target aware), skipping preconstructed receivers for derived `new`, and falling back to the captured super `this` when the environment TDZs `this`. The resizable-buffer constructor test `built-ins/TypedArrayConstructors/ctors/typedarray-arg/src-typedarray-resizable-buffer.js` now passes in both strict/sloppy.
- `%TypedArray%.prototype` iterators now ValidateTypedArray on entry, have correct property descriptors/length/name, and are non-constructors. Array iterator `next` skips out-of-bounds checks once exhausted and `byteOffset` reports 0 when detached/out-of-bounds. `TypedArray_prototype_entries/keys/values` resizable-buffer slices are passing.
- Array `find`/`findIndex`/`findLast`/`findLastIndex` call the callback for every index in the initial `length` (including holes) and behave correctly with mutations. TypedArray gained spec-compliant `find`/`findIndex`/`findLast`/`findLastIndex`, including ValidateTypedArray upfront and tolerant mid-iteration detach/out-of-bounds (IntegerIndexedElementGet semantics). All related Test262 groups now green.
- Eval early errors for class field initializers now traverse nested function/object bodies: we reject `super` calls/references, `arguments` (direct eval), and `new.target` (direct and indirect eval) when they appear anywhere in the eval code while running inside a field initializer.
- Class field computed property names now resolve at class definition time; non-callable or non-primitive `@@toPrimitive` results throw TypeError (including computed-name-toprimitive Test262 cases).
- Private name lookups now honor the branded scope encoded in resolved keys (e.g. `#x@id` no longer falls back to the innermost scope), and class member functions use the source private identifier as their display name, so private method `name` properties no longer leak the internal brand suffix.
- Anonymous function/class initializers for class fields (instance and static) now pick up the field’s lexical name (including private names), and optional chaining over private fields short-circuits correctly across chained accesses.
- Private accessors without a getter/setter now throw a proper JS `TypeError` with the active realm (instance/prototype creation stamps RealmState), and shadowing tests like `private-setter-shadowed-by-getter-on-nested-class` are green alongside the intercalated computed field cases.
- `Expressions_delete` is green again: sloppy globals now create deletable global properties (no declarative binding), super deletes throw ReferenceError, and deleting null/undefined targets throws TypeError.
- For-of loops now return `undefined` when the body completion is empty, and destructuring initializers in for-of heads allow `in` expressions (fixed `obj-prop-elem-init-in` parse error).
- Destructuring iterator error propagation is in place for array bindings/assignments: iterator property getters use the evaluation context, `TryGetIteratorForDestructuring` aborts with the pending throw, and iterator throws no longer trigger `IteratorClose` for assignment patterns (`Expressions_arrowFunction_dstr` and `Expressions_assignment_dstr` are green).

## Next Iteration Plan
1. Re-run the Language suite to refresh the current failure set (previously `Statements_forOf` looked hottest) now that destructuring is fixed.
2. Chase the top remaining cluster (likely for-of): ensure iterator result validation matches spec and string iteration walks UTF-16 code units (astral/truncated pairs).
3. Use realm logger traces and focused internal tests to pin any remaining stragglers, then circle back to residual Array/TypedArray gaps surfaced by the refreshed run.

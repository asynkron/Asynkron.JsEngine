# Language Suite Next Steps

## Current State
- Array built-ins are split into focused partials and `%Array.prototype%` is generator-backed; build stayed green with that layout.
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

## Next Iteration Plan
1. Re-run the Language suite to surface any remaining failures (class elements should now be green). Prioritize any clusters that still trip realm/private-name handling.
2. If failures remain in other areas, use the realm logger to trace binding/property resolution at the failing points and add focused debug tests where helpful.
3. Once the failure list is small again, clean up any remaining Array/TypedArray/property enumeration gaps.

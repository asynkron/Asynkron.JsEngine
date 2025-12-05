# Language Suite Next Steps

## Current State
- Array built-ins are split into focused partials and `%Array.prototype%` is generator-backed; build stayed green with that layout.
- Eval direct/indirect handling is spec-aligned again (including Annex B quirks).
- TypedArray constructors honor new-target for prototype resolution, set `name`, and `TypedArray.from` uses the calling constructor for targets. Resizable length logic goes through `ComputeLength` and iterator/enumeration flows respect detached/out-of-bounds views.
- Derived typed array constructors were returning `undefined`; fixed by routing host super calls through `InvokeWithContext` (new-target aware), skipping preconstructed receivers for derived `new`, and falling back to the captured super `this` when the environment TDZs `this`. The resizable-buffer constructor test `built-ins/TypedArrayConstructors/ctors/typedarray-arg/src-typedarray-resizable-buffer.js` now passes in both strict/sloppy.
- `%TypedArray%.prototype` iterators now ValidateTypedArray on entry, have correct property descriptors/length/name, and are non-constructors. Array iterator `next` skips out-of-bounds checks once exhausted and `byteOffset` reports 0 when detached/out-of-bounds. `TypedArray_prototype_entries/keys/values` resizable-buffer slices are passing.
- Array `find`/`findIndex`/`findLast`/`findLastIndex` call the callback for every index in the initial `length` (including holes) and behave correctly with mutations. TypedArray gained spec-compliant `find`/`findIndex`/`findLast`/`findLastIndex`, including ValidateTypedArray upfront and tolerant mid-iteration detach/out-of-bounds (IntegerIndexedElementGet semantics). All related Test262 groups now green.
- Eval early errors for class field initializers now traverse nested function/object bodies: we reject `super` calls/references, `arguments` (direct eval), and `new.target` (direct and indirect eval) when they appear anywhere in the eval code while running inside a field initializer.

## Next Iteration Plan
1. Re-run the `LanguageTests.Expressions_class_elements` slice to confirm the broader early-error traversal fixes `super`/`new.target`/`arguments` eval failures in class fields.
2. If failures remain, compare the thrown error types to spec expectations (SyntaxError vs TypeError) and expand the traversal to any missed AST shapes (e.g., class expressions/static blocks) with realm logger breadcrumbs.
3. Once class-element eval errors are green, return to the outstanding resizable Array/TypedArray enumeration/ownKeys failures and retune any noisy realm logging.

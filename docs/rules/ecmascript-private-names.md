# ECMAScript Private Names

When changing class elements, function creation, hoisting, dynamic-code parsing,
or IR execution context setup, treat private names as lexical scope state.

## Rules

1. Enter captured private-name scopes and the callable's own private-name scope
   before any path materializes nested function objects that can close over
   `#name` bindings.
2. Check creation-time paths as well as invocation-time paths. Hoisted function
   declarations are created during function declaration instantiation, before
   ordinary body execution reaches their source position.
3. Do not fix private-name misses by special-casing private member access
   against the receiver. The receiver proves brand membership; resolving the
   private-name key belongs to the lexical private-name environment.
4. Preserve innermost private-name precedence when nested classes reuse the same
   private identifier.
5. Keep ordinary top-level `ParseProgram` private-name early-error checks
   scope-empty, but do not force direct eval or Function-constructor parsing
   through that empty-scope check. Dynamic-code entry points must parse first
   and then run the private-name validation that owns their caller or empty
   scope semantics.
6. Prove fixes with focused coverage for instance and static elements, private
   fields, methods, getters, setters, nested duplicate private names, and
   field-initializer function expressions. When touching parse-time validation,
   also include direct eval inside a class method and the owning Test262 method
   group when the issue came from Test262.
7. Keep runtime private-field entries descriptor-typed. `JsObjectState`
   `PrivateFields` stores `PropertyDescriptor` values for private data fields
   and private accessors; do not reintroduce mixed `object?` slots or fallback
   branches such as `JsValue.FromObjectUnsafe(slot)` for private-slot reads.
   When touching this storage, prove the carrier with a scoped before/after
   search for `PrivateFields`, `Dictionary<string, object?>`, and
   `JsValue.FromObjectUnsafe(slot)`, plus focused private field/accessor tests.

## Why

Issue #776 / PR #957 fixed the `Expressions_class_elements` Test262 cluster
after IR function declaration instantiation created hoisted ordinary inner
functions without entering the class private-name scopes that were active at the
creation site. Invocation-time scope capture was not enough: the function object
itself had already been created without the lexical private-name environment.

Issue #1835 / PR #1855 fixed a `Statements_block_earlyErrors` crash by adding
parse-time private-name validation for ordinary scripts, then review exposed
that direct eval and the Function constructor already have specialized
scope-aware validation. Running those dynamic-code paths through the new
empty-scope top-level validator would reject valid direct eval inside class
methods before the caller's private-name scopes can be applied.

Issue `autrun-disl2i2p0adk-86311e5aad` / PR #2115 removed a legacy
`object?` carrier from `JsObjectState.PrivateFields`. All real writers were
already storing `PropertyDescriptor` entries, but the mixed dictionary type kept
dead object fallback reads alive and made private-field clone/read/write paths
carry avoidable object-boxing compatibility code. Private fields and accessors
must remain descriptor-backed runtime state, while private brands stay separate
identity state.

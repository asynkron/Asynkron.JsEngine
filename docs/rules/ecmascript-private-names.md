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
8. When admitting class literals or class expressions to resumable unified
   bytecode, materialize the class through the captured calling environment and
   synchronize VM slots into that environment before class creation. Private
   field initializers, `this.#field` reads, `#field in receiver` checks, private
   methods, and private accessors must remain owned by the existing
   class-definition/private-name machinery, with no VM fallback to AST or IR
   evaluation. Admit private methods/accessors only for the narrow
   environment-safe shape whose constructor and private member bodies do not
   capture resumable activation bindings. Prove the route with an actual
   suspension boundary and route-hit assertion, plus no-route proof for
   activation-slot captures and neighboring class-element families.
9. When admitting class constructors to production unified bytecode, decline any
   constructor callable that carries a private-name scope or captured
   private-name scopes until the constructor activation bridge owns that lexical
   state directly. Do not rely only on private expression operations in the
   constructor body: a private-brand-only class can have an otherwise public
   constructor body while still requiring class private-name state for branding
   and private member access. Prove future widening with both direct private
   field/method constructor use and private-brand-only base and derived
   constructor neighbors.

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

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-b314bd0632`
/ PR #3201 admitted the B24e resumable class-expression private-field slice.
The reusable decision was to keep `LoadClassLiteral` environment-backed:
resumable execution uses `state.CallingEnvironment`, syncs unified slots around
class creation, and delegates private-scope and brand setup to
`CreateClassValueFromLiteral` / class-definition machinery. The proof covered a
generator that suspends before returning a class with a private instance field,
then checks both `this.#value` and `#value in receiver` through the
`unified-bytecode-resumable-generator-fast-path` route.

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7d0f3d6a80`
/ PR #3194 admitted the B24f resumable class-expression private
method/accessor slice. The reusable decision was to keep private member
definition and invocation in the class-definition/private-name machinery while
admitting only class literals whose constructor and private member bodies do not
capture resumable activation slots. Activation captures still decline until a
future route materializes the function body environment that private member
callables would close over.

Issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-02-cla-ec8d71f1da`
/ PR #3321 hard-quarantined A7 private-name class constructors from production
unified bytecode. The repair added private-name scope checks to both base and
derived class-constructor route predicates after stale construct-call tests had
expected private-brand classes to route. The durable lesson is that constructor
admission must check callable private-name lexical state, not just whether the
constructor body contains a private expression op.

2026-06-07 update: that quarantine is superseded for A7 constructor activation.
The production constructor bridge now proves private-name lexical-state setup:
it initializes private brands/fields and enters own/captured private-name scopes
before VM execution. Direct private writes in base/derived constructors and
private-brand-only constructors may route through production bytecode. Plain
private reads used as nested binary/RHS value operands and single-hop optional
private reads (`receiver?.#field`) now route through the A51f5 partial
private-neighbor admission. Private receiver-prefix named calls such as
`receiver.#child.value()` now route too, preserving the method receiver after the
private prefix read. Chained optional-private receiver-neighbor, delete-defense,
mutation-neighbor, and super-neighbor diagnostics remain tracked by A51f5.

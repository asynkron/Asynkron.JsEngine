# ECMAScript Private Names

When changing class elements, function creation, hoisting, or IR execution
context setup, treat private names as lexical scope state.

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
5. Prove fixes with focused coverage for instance and static elements, private
   fields, methods, getters, setters, nested duplicate private names, and
   field-initializer function expressions, then run the owning Test262 method
   group when the issue came from Test262.

## Why

Issue #776 / PR #957 fixed the `Expressions_class_elements` Test262 cluster
after IR function declaration instantiation created hoisted ordinary inner
functions without entering the class private-name scopes that were active at the
creation site. Invocation-time scope capture was not enough: the function object
itself had already been created without the lexical private-name environment.

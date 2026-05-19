# ADR 0010: Keep private-name scope capture on function instantiation

## Status

Accepted

## Context

Issue #776 fixed the Test262 `Expressions_class_elements` failures where
ordinary inner functions created inside class elements could not resolve the
class private names that were lexically in scope at their creation site.

The defect was specific to the IR function-declaration instantiation path. The
runtime already carried captured private-name scopes for function invocation,
but hoisted function declarations are materialized during function declaration
instantiation. That temporary instantiation context did not enter the captured
and own private-name scopes, so inner ordinary functions created inside instance
methods, static methods, accessors, and field initializers lost access to
`#name` bindings even though ECMAScript treats private names as lexical state.

## Decision

Treat class private names as lexical execution context state that must be
present both when a function object is created and when it is invoked.

IR function-declaration instantiation must enter the callable's captured
private-name scopes and own private-name scope before hoisted function
declarations are materialized. This keeps hoisted ordinary functions consistent
with function expressions, arrow functions, and later invocation-time private
name resolution.

Do not repair private-name misses by special-casing private member lookup at
the access site or by binding private names to receivers. The receiver only
proves the brand; the private-name key is resolved from the lexical private-name
environment.

## Consequences

- Future changes to function object creation must check both creation-time and
  invocation-time private-name scope propagation.
- Tests for private-name closure bugs should cover instance and static class
  elements, fields, methods, getters, setters, nested duplicate private names,
  and field-initializer function expressions.
- Reviewers should inspect hoist-time paths such as function declaration
  instantiation, not only direct function expression creation or call-time
  invocation.

# ADR 0128: Keep private-name parse validation entrypoint owned

## Status

Accepted

## Context

Issue #1835 / PR #1855 fixed the Test262
`Statements_block_earlyErrors` case
`language/statements/block/early-errors/invalid-names-member-expression-this.js`.
The failing source shape, `{ this.#x }`, reached runtime because ordinary
script parsing did not run private-name early-error validation after typed parse.

The first delivery added a normal `ParseProgram` validation hook using
`PrivateNameValidator.FindInvalidPrivateName(..., ImmutableArray<PrivateNameScope>.Empty)`.
Review then found the important boundary: `ParseProgram` is also used by dynamic
code entry points. Direct eval can inherit caller private-name scopes, while the
Function constructor must validate against its own empty dynamic-function scope
and convert parser errors into JavaScript `SyntaxError` objects. A single
unconditional empty-scope validator in `ParseProgram` would reject valid direct
eval such as `eval("this.#x")` inside a class method before `EvalHostFunction`
can apply the caller's captured private-name scopes.

## Decision

Keep private-name parse validation owned by the entry point that knows the
applicable scope semantics.

Ordinary script/module `ParseProgram` should validate parsed programs with an
empty private-name scope by default so parse-negative Test262 cases fail before
runtime execution. Dynamic-code callers that need specialized semantics must opt
out of that default and run their existing validation path:

- direct eval parses with top-level private-name validation disabled, then
  validates using the captured caller private-name scopes;
- the Function constructor parses with top-level private-name validation
  disabled, then validates with its explicit empty dynamic-function scope and
  JavaScript error conversion;
- ordinary top-level parse/evaluate paths keep the default empty-scope early
  error check.

Do not repair future private-name parse failures by making the shared parser
less strict or by removing dynamic-code scope validation. The shared parse hook
is a convenience for ordinary source goals; direct eval and Function constructor
remain semantic entry points with their own validation authority.

## Consequences

- Any future `ParseProgram` validation added after typed parse must audit direct
  eval, indirect eval if applicable, Function constructor, module parsing, and
  tooling/test callers before becoming unconditional.
- Private-name regressions need both top-level parse-negative coverage and
  dynamic-code coverage when the changed hook is shared. For this incident, the
  focused proof included `{ this.#x }`, valid class private member access, and
  direct eval inside a class method.
- The owning Test262 proof for this class remains the relevant method group,
  such as `Name=Statements_block_earlyErrors`, plus focused internal tests for
  valid dynamic-code inherited-scope behavior.
- This complements ADR 0010, which keeps private names as lexical scope state
  during function creation/invocation, and ADR 0015, which keeps direct eval's
  caller lexical context explicit.

# ECMAScript Template Object Cache Identity

When changing tagged-template evaluation, expression-bytecode template payloads,
`RealmState.TemplateObjectCache`, or eval program caching, preserve ECMAScript
template-object identity semantics before optimizing cache reuse.

## Rules

1. Template object cache identity is callsite identity, not source text,
   cooked-string arrays, raw-string arrays, or tag function identity. The same
   parsed callsite must reuse its template object, and a distinct parsed
   callsite must not accidentally share one.
2. Treat expression-bytecode `TaggedTemplateDescriptor` constants as
   parse/compiled-payload identity for template-object caching. Reusing a
   cached eval `ProgramNode` also reuses those descriptor instances.
3. Eval source that may contain a template literal must either parse fresh for
   each eval instantiation or carry a separately proven eval-instantiation
   identity into the template-object cache key. A source-text-only eval program
   cache is not enough for tagged-template callsites.
4. Do not repair eval tagged-template identity failures by globally keying
   `TemplateObjectCache` by template string contents. That would collapse
   distinct callsites that happen to have the same cooked/raw text and would
   weaken ordinary template-object caching semantics.
5. Prove this surface with both sides of the behavior: focused sloppy and
   strict eval-created-inner-function regressions, plus coverage that ordinary
   non-eval same-callsite tagged-template caching remains intact. For issue
   #2563 / PR #2569, the owning Test262 proof was:
   ```bash
   rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
     --filter "FullyQualifiedName~Expressions_taggedTemplate&FullyQualifiedName~cache-eval-inner-function"
   ```

## Why

Issue #2563 / PR #2569 fixed the Test262
`language/expressions/tagged-template/cache-eval-inner-function.js` strict and
sloppy failures. The regression came from `EvalHostFunction` reusing a cached
`ProgramNode` for identical eval source. That was safe for repeated parse and
plan work generally, but unsafe for tagged templates because the realm template
object cache keys by parse-node/descriptor identity. Separate eval parses of
the same source must produce distinct tagged-template callsites, while ordinary
code still needs same-callsite reuse.

Related ADR:

- `docs/adrs/0185-keep-direct-eval-program-cache-strictness-and-caller-context-owned.md`

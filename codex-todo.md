# Codex TODO

- [x] Skip block environment allocation when hoist plan shows no lexical/function decls
- [x] Avoid per-loop environment for var-only for-bodies
- [x] Pool/reuse per-iteration environments when loop bindings aren’t captured
- [ ] Reuse JsEnvironment instances from a small pool for block/loop/param envs
- [x] Parse once and reuse ProgramNode/ParsedProgram in benchmarks to measure execution only
- [ ] Identifier cache fast path when no dynamic scope (direct eval/with)
- [ ] Cheaper binding storage for tiny envs (small-map/slots for ≤4 bindings)
- [ ] Argument array pooling + inline small-arg call path audit
- [ ] Microtask/event-loop laziness for purely sync code

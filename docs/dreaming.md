# Asynkron.JsEngine Dreaming

Date: 2026-05-27

## Why this document exists
This is the architecture north star for Asynkron.JsEngine as a Node.js-competitive JavaScript runtime on .NET. It describes the target product/system shape first, then modules, components, and subcomponents. It complements the roadmap by keeping long-lived intent explicit.

## Short critique of current documentation state
Before this file, the repository had a detailed roadmap and architecture deep dive but no dedicated dream document. That made it easy to accumulate optimization slices and ADR boundaries without a single top-down destination map. The risk is a technically rich ledger of improvements that drifts from product-level coherence.

## Product dream
Asynkron.JsEngine is a standards-first, production-grade JavaScript runtime for .NET with:
- broad ECMAScript compatibility (including long-tail semantics, not just happy paths)
- predictable Node.js-style host integration and module behavior
- evidence-driven performance that stays competitive under real workloads
- explicit architectural boundaries that allow aggressive optimization without semantic regressions

## System dream (top-level modules)
1. Language Frontend
2. Compiler Pipeline
3. Execution Runtime
4. Async and Concurrency Runtime
5. Module and Host Interop Runtime
6. Standard Library and Built-ins
7. Observability, Testing, and Performance Governance

## 1) Language Frontend
### Goal
Turn source text into typed semantic structures with strict, deterministic parse/analyze behavior.

### Components
- Lexer and tokenization
- Parser and typed immutable AST
- Early strictness/module rule validation
- AST-local semantic caches

### Subcomponents
- token scanning for strings/regex/templates and diagnostics
- parse pipelines for program/statement/expression/function/class
- static semantic analysis for hoisting, bindings, and dynamic-scope risk flags
- reusable AST cache entries for repeated compile/execution paths

## 2) Compiler Pipeline
### Goal
Lower typed AST into execution artifacts designed for predictable performance.

### Components
- Statement IR lowering
- Expression bytecode lowering (`ExpressionProgram` + `ExpressionOp`)
- Plan eligibility and fallback classification
- Static slot/layout assignment

### Subcomponents
- `ExecutionPlanBuilder` + statement-family emitters
- expression compiler for stack-machine payloads and pool packing
- control-flow and completion-shape lowering
- flat-slot mapping and layout identity
- unsupported-family diagnostics and migration backlog tracking

## 3) Execution Runtime
### Goal
Execute proven-safe plans on a VM-like path, while preserving exact JavaScript semantics where fallback is required.

### Components
- ExecutionPlan runner VM
- Expression bytecode interpreter
- Environment/slot machinery
- Completion and abrupt-flow machinery
- Dynamic/eval fallback path

### Subcomponents
- instruction dispatch and hot-path handlers
- bytecode stack, flags, and assignment-reference side state
- lexical/variable/object environment composition
- return/throw/break/continue/finally restart handling
- safety-gated AST eval seams for dynamic `with`/direct `eval`/unsupported surfaces

## 4) Async and Concurrency Runtime
### Goal
Provide reliable promise, microtask, timer, and generator behavior with deterministic scheduling contracts.

### Components
- Promise jobs and microtask queue
- async function and async generator execution machinery
- await scheduling and resume routing
- timer/event-queue host bridge

### Subcomponents
- `AwaitScheduler` and task/promise bridging
- generator/async state carriers and resume modes
- async-step orchestration for IR and fallback paths
- callback surfaces for host-driven wakeups

## 5) Module and Host Interop Runtime
### Goal
Expose practical Node.js-style interoperability while keeping standards boundaries clear.

### Components
- ES module loading and evaluation
- dynamic import and module registry
- host function bridge and global registration APIs
- compatibility shims for host packages

### Subcomponents
- module instantiate/evaluate lifecycle
- import namespace and `import.meta` ownership
- JSON module support and top-level await behavior
- host callable adapters and error translation seams

## 6) Standard Library and Built-ins
### Goal
Deliver high-fidelity built-in behavior with explicit semantics ownership and performance-safe fast paths.

### Components
- core object model (`JsValue`, `JsObject`, descriptors/prototypes)
- built-in constructors/prototypes (Array, String, Promise, Proxy, Intl, Temporal, etc.)
- specialized storage/runtime types (arrays, typed arrays, regexp, maps/sets)

### Subcomponents
- JsValue-native helper surfaces on hot paths
- descriptor/property semantics and strictness enforcement
- built-in algorithm implementations with ADR-guarded behavior boundaries
- cross-realm correctness and brand validation surfaces

## 7) Observability, Testing, and Performance Governance
### Goal
Make optimization and compatibility work provable, repeatable, and regression-resistant.

### Components
- Test262 and focused regression packs
- internal quality gate and deterministic build/test surfaces
- profiling and benchmark infrastructure
- ADR and roadmap evidence governance

### Subcomponents
- subsystem-focused proof packs and targeted reruns
- canonical quality gate (`make quality`) for internal confidence
- profile runner and benchmark matrix for CPU/allocation trends
- architecture/roadmap/ADR sync discipline

## Greenfield target vs current reality
The target architecture is intentionally ambitious. Current repository evidence indicates important progress and explicit seams:
- Strong current direction exists: typed AST + statement IR + expression bytecode fast path.
- Unified bytecode production routing exists but is still bounded by eligibility/parity guardrails.
- Dynamic/eval and unsupported statement families are still active constraints.
- Async generator resume and related runtime seams remain known weak spots.
- Performance progress is real but profile-owned; claims must remain evidence-backed and workload-specific.

This means the dream is not "already done". It is a deliberate destination with explicit gap ownership.

## Non-goals for this document
- It does not claim current full production parity with Node.js.
- It does not replace detailed ADR decisions, profile reports, or roadmap issue tracking.
- It does not redefine current implementation constraints; it frames where those constraints should lead.

## Operating principle
Prefer architecture that keeps semantics explicit, optimization local, and evidence mandatory: every fast-path expansion should have a clear ownership boundary, focused proof coverage, and profile/test signals that justify its existence.

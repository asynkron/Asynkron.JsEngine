# JsEngine Rules Index

Durable preventive rules distilled from incidents and learn-stage analysis.

**These are on-demand.** They are kept out of `.claude/rules/` (which Claude
Code auto-loads in full — ~167K tokens — and was overflowing agent prompts)
so sessions stay within the context window. Before working on a subsystem,
scan this index and read **only** the rule files whose scope matches your task.

| Rule file | Topic |
|---|---|
| `adr-allocation.md` | ADR Allocation |
| `agent-context-issues.md` | AgentContext: Persistent Memory via GitHub Issues |
| `async-resume-callback-ownership.md` | Async Resume Callback Ownership |
| `async-runtime-tests.md` | Async Runtime Tests |
| `command-line-solution-builds.md` | Command-Line Solution Builds |
| `csharp-editing.md` | C# File Editing Rules |
| `dependency-maintenance.md` | Dependency Maintenance |
| `ecmascript-abstract-operations.md` | ECMAScript Abstract Operation Order |
| `ecmascript-annex-b-block-functions.md` | ECMAScript Annex B Block Functions |
| `ecmascript-binding-name-inference.md` | ECMAScript Binding Name Inference |
| `ecmascript-direct-eval-declaration-instantiation.md` | ECMAScript Direct Eval Declaration Instantiation |
| `ecmascript-error-constructors.md` | ECMAScript Error Constructors |
| `ecmascript-intl-language-tags.md` | ECMAScript Intl Language Tags |
| `ecmascript-labeled-statements.md` | ECMAScript Labeled Statements |
| `ecmascript-modules.md` | ECMAScript Modules |
| `ecmascript-numeric-coercions.md` | ECMAScript Numeric Coercions |
| `ecmascript-numeric-literals.md` | ECMAScript Numeric Literals |
| `ecmascript-private-names.md` | ECMAScript Private Names |
| `ecmascript-proxy-realm-errors.md` | ECMAScript Proxy Realm Errors |
| `ecmascript-regexp-runtime-bridges.md` | ECMAScript RegExp Runtime Bridges |
| `ecmascript-regexp-unicode-properties.md` | ECMAScript RegExp Unicode Properties |
| `ecmascript-template-object-cache.md` | ECMAScript Template Object Cache Identity |
| `expression-bytecode-assignment.md` | Expression Bytecode Assignment Semantics |
| `expression-bytecode-ast-seams.md` | Expression Bytecode AST Seam Classification |
| `expression-bytecode-call-targets.md` | Expression Bytecode Call Targets |
| `expression-bytecode-ir-payloads.md` | Expression Bytecode IR Payload Guardrails |
| `expression-bytecode-meta-bindings.md` | Expression Bytecode Meta Bindings |
| `expression-bytecode-packing.md` | Expression Bytecode Packing |
| `function-activation-proof-pack.md` | Function Activation Proof Pack |
| `generator-execution-path-parity.md` | Generator Execution Path Parity |
| `host-function-observable-shape.md` | Host Function Observable Shape |
| `ir-control-flow-cleanup.md` | IR Control-Flow Cleanup |
| `js-spec-property-access.md` | JavaScript Spec Property Access in C# Helpers |
| `jsvalue-core-values.md` | JsValue Core Runtime Values |
| `native-function-source.md` | Native Function Source Metadata |
| `performance-profiling-guardrails.md` | Performance Profiling Guardrails |
| `pre-pr-required.md` | Pre-PR Checklist (MANDATORY) |
| `proper-tail-calls.md` | Proper Tail Calls |
| `recurring-maintenance-child-runs.md` | Recurring Maintenance Child Runs |
| `roadmap-architecture-claims.md` | Roadmap Architecture Claims |
| `statement-bytecode-packing.md` | Statement Bytecode Packing |
| `switch-lowering-completion.md` | Switch Lowering Completion |
| `test262-agent-atomics-async.md` | Test262 Agent and Atomics Async Lifecycle |
| `test262-harness-policy.md` | Test262 Harness Policy |
| `test262-solution-build-boundary.md` | Test262 Solution Build Boundary |
| `test262-triage-proof.md` | Test262 Triage Proof |
| `tooling-shell-wrappers.md` | Tooling Shell Wrappers |
| `unified-bytecode-prototypes.md` | Unified Bytecode Prototypes |
| `uri-percent-decoding.md` | URI Percent-Decoding |

When a lesson is genuinely new and no existing file is an accurate home, add
a new rule file here and a row above. Otherwise merge into the matching
domain rule, preserving the WHY and issue/incident traceability.

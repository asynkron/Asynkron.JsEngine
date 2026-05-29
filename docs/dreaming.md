# Asynkron.JsEngine Dreaming

Date: 2026-05-29

## Why this document exists
Architecture north star for Asynkron.JsEngine as a Node.js-competitive JavaScript runtime on .NET.

- Who: maintainers making recurring optimization, compatibility, and module/runtime decisions.
- What: a coherent greenfield target architecture from system level down to subcomponents.
- Why: keep semantics, runtime ownership, and evidence discipline aligned across slices.

## Critique of the previous dream state

The 2026-05-28 dream was a significant improvement over its predecessor — it introduced a runtime spine, orthogonal control planes, and an explicit capability lifecycle. The remaining weak points were:

1. **No clear compilation pipeline diagram.** The path from source text to a runnable program artifact was described in text but never drawn as data-flow. Recurring slices had to re-derive it.
2. **The dual-VM endgame was implicit.** The statement IR VM and expression/unified bytecode VM are conceptually separate runtimes that share an environment model, but this was buried in component prose rather than expressed as a first-class architecture boundary.
3. **"Eliminate all AST fallback seams" was the implicit goal but never stated as an architectural end-state.** Fallback seams are residual technical debt, not design features. Making that explicit changes how recurring slices are framed.
4. **JsValue as the universal runtime currency was underemphasized.** The value model is the most pervasive cross-module contract; giving it its own diagram surface reduces drift.
5. **Evidence fabric was last by convention, not by importance.** Evidence should be drawn as a horizontal layer that governs every module, not as a downstream terminus.

This revision keeps all proven constraints from the prior dream, but adds: a compilation pipeline data-flow diagram, a dual-VM execution model diagram, a JsValue contract diagram, and an explicit end-state for "seam elimination complete." It also promotes Evidence from a downstream node to a horizontal governance layer.

## Product dream
Build a standards-first, production-grade JavaScript Runtime Fabric on .NET that is:

- **compilation-first:** source is compiled to typed bytecode artifacts; AST is never in the runtime hot path.
- **dual-VM coherent:** statement IR VM and expression bytecode VM share one environment model and one completion protocol.
- **seam-free by design:** every AST fallback seam is a temporary correctness bridge, not a permanent design choice; the architecture tends toward their elimination.
- **value-model-native:** `JsValue` is the universal runtime currency; object-overload seams are temporary compat shims.
- **evidence-governed:** every correctness and performance claim is non-deliverable until focused proof plus canonical quality gate evidence exists.

## Top-level system (greenfield)

Top-level thing: **JavaScript Runtime Fabric**.

```mermaid
flowchart TD
    SRC[JavaScript Source Text]

    subgraph COMP[Language Compiler]
        direction TB
        LEX[Lexer / Parser]
        AST[Typed Immutable AST]
        LOW[IR + Bytecode Lowering]
        ELG[Eligibility Classifier]
        ART[Program Artifacts]
    end

    subgraph ENG[Execution Engine]
        direction TB
        SVM[Statement IR VM]
        XVM[Expression / Unified Bytecode VM]
        ENV[Environment and Slot Model]
        CPL[Completion and Abrupt Flow]
        FBK[Fallback Bridge — temporary seam]
    end

    subgraph CON[Concurrency Runtime]
        direction TB
        MQ[Microtask Queue]
        AWR[Await / Resume Machinery]
        AGN[Async Generator State Machine]
        HWK[Host Wakeup Bridge]
    end

    subgraph PLT[Platform Layer]
        direction TB
        ESM[ESM Loader + Module Registry]
        DYN[Dynamic Import Pipeline]
        HCB[Host Callable Bridge]
        ADP[Compatibility Adapters]
    end

    subgraph STD[Standard Library]
        direction TB
        JSV[JsValue / JsObject Core Model]
        PRC[Prototype Chain + Built-ins]
        DSC[Descriptor System]
        SPC[Specialized Storage]
    end

    subgraph EVD[Evidence and Governance — horizontal layer]
        direction LR
        T62[Test262 + Focused Packs]
        PRF[ProfileRunner Matrix]
        QGT[Canonical Quality Gate]
        ADR[ADR and Roadmap Traceability]
    end

    SRC --> COMP
    COMP --> ENG
    ENG --> CON
    ENG --> PLT
    ENG --> STD
    EVD -.governs.-> COMP
    EVD -.governs.-> ENG
    EVD -.governs.-> CON
    EVD -.governs.-> PLT
    EVD -.governs.-> STD
```

## Compilation pipeline (data flow)

This is the heart of the architecture. Every optimization slice touches this pipeline. The rule is: **artifacts move forward; the AST stops at the lowering stage.**

```mermaid
flowchart LR
    SRC[Source text]
    LEX[Lexer → Token stream]
    PAR[Parser → Typed AST]
    ANA[Static analysis\nbinding / hoisting / dynamic-scope flags]
    SIR[Statement IR lowering\nExplicitExecutionPlan]
    ELW[Expression lowering\nExpressionProgram / ExpressionOp]
    UBC[Unified bytecode compilation\nUnifiedBytecodeProgram]
    ELC[Eligibility classifier\nroutes: unified / expression / fallback]
    SLT[Slot and layout assignment\nplan-owned]

    SRC --> LEX --> PAR --> ANA
    ANA --> SIR
    ANA --> ELW
    ANA --> UBC
    SIR --> SLT
    ELW --> ELC
    UBC --> ELC

    ELC -->|accepted unified shapes| UBP[UnifiedBytecodeProgram artifact]
    ELC -->|accepted expression shapes| EXP[ExpressionProgram artifact]
    ELC -->|unsupported shapes| FBK[Fallback marker — no artifact]

    style FBK fill:#c00,color:#fff
```

Pipeline invariants:
- The AST is consumed at the lowering stage and must not appear as a runtime argument in the VMs.
- Eligibility classifiers are the only thing allowed to inspect compiled shape boundaries at runtime.
- Every `FBK` marker is tracked technical debt. The architecture goal is to reduce it toward zero.

## Dual-VM execution model

The runtime runs two cooperating virtual machines that share one environment and one completion protocol.

```mermaid
flowchart TB
    subgraph ExecutionEngine["Execution Engine"]
        subgraph SVM["Statement IR VM\n(ExplicitExecutionPlan runner)"]
            SH[Statement handler dispatch]
            BP[Breakable frame stack]
            SC[Scope entry / exit]
        end

        subgraph XVM["Expression Bytecode VM\n(UnifiedBytecodeProgram / ExpressionProgram)"]
            OP[Opcode dispatch loop]
            VS[Typed value stack]
            OC[Optional-chain short-circuit state]
        end

        subgraph ENV["Shared Environment Model"]
            SL[Flat slot array]
            LX[Lexical environments]
            OB[Object environments — with / catch]
            GBL[Global binding root]
        end

        subgraph CPL["Shared Completion Protocol"]
            RT[Return]
            TH[Throw]
            BR[Break / Continue + labels]
            FN[Finally restart chain]
        end

        FBK["Fallback Bridge\n(AST eval — temporary)"]
    end

    SVM --> XVM
    SVM --> ENV
    XVM --> ENV
    SVM --> CPL
    XVM --> CPL
    SVM -.residual seam.-> FBK
    FBK -.must shrink toward zero.-> SVM

    style FBK fill:#c00,color:#fff
```

Dual-VM invariants:
- The two VMs share one environment model and one completion protocol. They do not share opcode tables.
- The statement IR VM is the outer loop; the expression VM is called from within statement handlers.
- The fallback bridge is a correctness shim for shapes not yet lowered. It is not a design feature.
- The endgame is: every statement shape has an IR instruction; every expression shape has a bytecode opcode; the fallback bridge is dead code.

## JsValue — the universal runtime currency

```mermaid
flowchart LR
    subgraph Values["JsValue domain"]
        UD[Undefined]
        NL[Null]
        BL[Boolean]
        NM[Number — double]
        ST[String — rope or interned]
        SM[Symbol]
        OB[Object reference]
    end

    subgraph Objects["JsObject payloads"]
        DO[Data object — property bag]
        AR[JsArray — dense storage]
        FN[JsFunction / closure]
        RX[JsRegExp]
        PX[JsProxy / Reflect]
        IT[Iterator / generator state]
    end

    subgraph Contract["Runtime contract"]
        NV[JsValue-native hot paths\nno object? boxing on fast paths]
        DS[Descriptor system\ndata / accessor / configurable / writable]
        BD[Brand validation\nIntl / TypedArray / etc.]
        CR[Cross-realm / realm-sensitive errors]
    end

    OB --> Objects
    Values --> Contract
    NV -.governs.-> DO
    NV -.governs.-> AR
    NV -.governs.-> FN
```

Value contract rules:
- Fast-path helpers must accept and return `JsValue`, not `object?`. Object-overload variants are obsolete compat shims and must be removed.
- Descriptor semantics, brand validation, and cross-realm error creation are Standard Library obligations, not Execution Engine bypasses.
- The rope string model controls flattening ownership; consumers drive flattening, the runtime does not eagerly flatten.

## Async concurrency model

```mermaid
flowchart TB
    subgraph AsyncRuntime["Concurrency Runtime"]
        MQ[Microtask Queue\nPromise jobs]
        AW[Await suspension point\nopcode in Expression VM]
        RS[Resume routing\nrestore slot state + restart]
        AG[Async Generator\nstate machine — yield*/next/return]
        HW[Host Wakeup Bridge\ncallback enqueue]
    end

    subgraph Contracts["Contracts"]
        SC[Suspension contract\nExecution VM emits await opcode → hands off to Concurrency]
        RC[Resume contract\nConcurrency restores context → re-enters Expression VM]
        HC[Host contract\nHost delivers callbacks to Microtask Queue only]
    end

    AW --> SC
    SC --> MQ
    MQ --> RS
    RS --> RC
    RC --> AW
    HW --> HC
    HC --> MQ
    AG --> AW
```

Async invariants:
- The suspension/resume boundary is an explicit opcode contract, not an implicit continuation.
- Async generator continuation state is fully owned by the Concurrency Runtime; the Execution Engine does not peek at generator internal state.
- Host callbacks always enter through the Microtask Queue boundary; they never directly re-enter the Execution Engine.
- The shared `ExecuteAsyncStep` bridge is a known seam (Milestone C follow-through). The target state is a dedicated async-generator IR executor.

## Module and host platform model

```mermaid
flowchart LR
    SRC[Module source]
    PAR[Parse → typed module AST]
    INS[Instantiate — namespace bindings]
    EVL[Evaluate — execute top-level]
    REG[Module Registry\nkeyed by specifier + realm]
    DYN[Dynamic import pipeline\nPhase 2 deferred execution]
    META[import.meta ownership\nhost-layer behavior]
    HCB[Host Callable Bridge\nJsFunction ↔ .NET delegate]
    ADP[Compatibility Adapters\nJSON modules, CommonJS shim boundary]

    SRC --> PAR --> INS --> EVL --> REG
    DYN --> REG
    META --> HCB
    HCB --> ADP

    style ADP fill:#665,color:#fff
```

Platform invariants:
- CommonJS behavior is a compatibility shim, not a core-engine contract. It lives in the Host Callable Bridge / Adapters layer.
- `import.meta` is host-layer behavior. The engine exposes the hook; the host owns the content.
- Module evaluation is async-aware by construction; top-level await is a first-class ESM concern.
- Node.js-competitive module/runtime parity language requires explicit proof; "directional" until then.

## End-state: seam elimination complete

This is what "done" looks like from a greenfield architecture perspective.

```mermaid
stateDiagram-v2
    [*] --> Active: seam exists

    state Active {
        [*] --> Tracked
        Tracked --> Proof_Sliced: owner module identified + test coverage exists
        Proof_Sliced --> IR_Lowered: lowering-time normalization delivers bytecode shape
        IR_Lowered --> Fallback_Deleted: fallback branch deleted, all tests green
        Fallback_Deleted --> [*]
    }

    Active --> Accepted: seam is intentional compat boundary\n(e.g. eval observability, dynamic with-scope)
    Accepted --> [*]: documented in ADR

    note right of Accepted: Some seams are correct by design.\nA with-statement scope chain cannot\nbe eliminated without breaking semantics.
```

Seam-elimination rules:
- Every fallback seam must be tracked with an owner module and test coverage before a slice can claim progress.
- Lowering-time normalization is preferred over runner-time special-cases. If a shape can be rewritten into existing bytecode at emit time, do that.
- When a seam is intentional (eval observability, dynamic `with`, proxy interceptors), document it in an ADR and stop treating it as debt.
- The canonical list of remaining fallback seams is maintained in the unified-bytecode expansion contract.

## Capability lifecycle (claim discipline)

```mermaid
stateDiagram-v2
    [*] --> Candidate
    Candidate --> Prototyped: owner-surface design accepted
    Prototyped --> ProvenScoped: focused semantics pack green + no regressions
    ProvenScoped --> ProvenWidened: owning-cluster proof green
    ProvenWidened --> PerfQualified: profile/benchmark evidence attached\nbaseline + final signal both present
    PerfQualified --> ProductionClaim: canonical quality gate green

    Prototyped --> Candidate: owner boundary changed
    ProvenScoped --> Prototyped: boundary wording drift detected
    ProvenWidened --> Prototyped: widened proof regression
    PerfQualified --> ProvenWidened: perf signal regression
    ProductionClaim --> Prototyped: ADR contract changed

    note right of PerfQualified: Node.js-competitive and CommonJS\nparity language cannot enter\nProductionClaim without passing\nall gates — no exceptions.
```

## Component ownership map (big to small)

### 1. Language Compiler

Goal: deterministic, side-effect-free source → artifact transformation.

```mermaid
flowchart TD
    subgraph LC["Language Compiler"]
        LEX[Lexer\ntokens + unicode categories]
        PAR[Parser\nrecursive descent → TypedAST]
        SSM[Static Semantics\nhoisting, binding analysis, dynamic-scope flags]
        SLW[Statement IR Lowering\nExplicitExecutionPlan builder families]
        ELW[Expression Lowering\nExpressionProgram / ExpressionOp encoding]
        UBC[Unified Bytecode Compiler\nUnifiedBytecodeCompiler opcodes]
        ELC[Eligibility Classifier\nshape tests → routing decision]
        SLA[Slot and Layout Assignment\nplan-owned, runtime-consumed]
    end

    LEX --> PAR --> SSM
    SSM --> SLW --> SLA
    SSM --> ELW --> ELC
    SSM --> UBC --> ELC
```

- Subcomponents: regex/template scanning, hoisting/binding analysis, completion-shape lowering, unsupported-family diagnostics.
- Key invariant: **The AST exits the compiler. It does not travel to the execution stage.**

### 2. Execution Engine

Goal: run proven shapes on fast paths; preserve semantics through explicit, tracked fallback seams.

```mermaid
flowchart TD
    subgraph EE["Execution Engine"]
        SIP[Statement IR VM\nhandler dispatch + breakable frames]
        XVM[Expression Bytecode VM\nopcode loop + typed value stack]
        ENV[Environment / Slot Model\nflat slots + lexical + object chains]
        CPL[Completion Protocol\nreturn / throw / break / finally restart]
        FBK[Fallback Bridge\nAST eval — tracked residual seam]
    end

    SIP --> XVM
    SIP --> ENV
    XVM --> ENV
    SIP --> CPL
    XVM --> CPL
    SIP -.shrink toward zero.-> FBK
```

- Subcomponents: instruction dispatch, optional-chain short-circuit state, lexical/object environment composition, return/throw/break/continue/finally restart semantics.

### 3. Concurrency Runtime

Goal: deterministic async behavior; scheduling and resume ownership are explicit, not implicit.

- Components: microtask queue, async function machinery, async generator state machine, await/resume routing, host wakeup bridge.
- Subcomponents: scheduler contracts, resume-mode carriers, callback ownership boundaries, continuation state.
- Key seam: `ExecuteAsyncStep` bridge is Milestone C follow-through; async generator needs a dedicated IR executor.

### 4. Platform Layer

Goal: Node-competitive interoperability without blurring engine vs host responsibility.

- Components: ESM lifecycle runtime, dynamic import pipeline, module registry, host callable bridge, compatibility adapters.
- Subcomponents: `import.meta` ownership, JSON module boundaries, top-level await integration, host error translation.
- Non-goal (until proven): CommonJS parity at the engine level. CJS behavior lives in the host adapter layer.

### 5. Standard Library

Goal: high-fidelity built-ins with runtime-owned semantics and JsValue-native fast paths.

- Components: JsValue/JsObject core model, prototype chain + constructors, descriptor system, specialized storage (JsArray, RegExp, Intl, Temporal).
- Subcomponents: descriptor semantics, strictness behavior, cross-realm/brand validation, JsValue-native hot-path helpers.
- Key invariant: object-overload variants are obsolete compat shims; JsValue-native is the target.

### 6. Evidence and Governance (horizontal layer)

Goal: keep every correctness and performance claim provable, repeatable, and traceable.

- Components: Test262 + focused proof packs, ProfileRunner matrix, canonical `make quality` gate, ADR/roadmap governance.
- Subcomponents: narrow proof packs, recurring profile loops (baseline + final signal), seam-scan queries, architecture traceability checks.
- Key invariant: Evidence governs all other modules. No claim advances past `ProvenScoped` without focused pack coverage.

## Cross-module routing map

When a recurring slice starts, use this map to find the primary owner and avoid blurring boundaries.

```mermaid
flowchart LR
    LC[Language Compiler] -->|typed artifacts| EE[Execution Engine]
    EE -->|suspension opcodes| CR[Concurrency Runtime]
    EE -->|module evaluation entry| PL[Platform Layer]
    EE -->|JsValue operations| SL[Standard Library]
    CR -->|resume handoff| EE
    PL -->|host functions| SL
    SL -->|runtime values| EE

    EV[Evidence — horizontal] -.governs.-> LC
    EV -.governs.-> EE
    EV -.governs.-> CR
    EV -.governs.-> PL
    EV -.governs.-> SL
```

Boundary contract rules:
- **LC → EE:** Compiler guarantees typed artifact invariants; no silent runner-time AST fallback widening.
- **EE → CR:** Suspension/resume boundaries are explicit opcode contracts; no implicit continuation capture.
- **EE → PL:** Module/host may adapt behavior; core evaluator semantics stay execution-owned.
- **EE → SL:** Built-in fast paths are JsValue-native; descriptor/brand semantics are Standard Library obligations.
- **CR → EE:** Resume always re-enters through a tracked opcode boundary, not through a shared runner bridge.
- **→ EV:** Every module reports to Evidence. No capability claim advances without an Evidence artifact.

## Proven-now vs directional-next

| Area | Proven now | Directional next (needs new proof) |
|---|---|---|
| Compilation pipeline | Typed AST → statement IR, ExpressionProgram, UnifiedBytecodeProgram | Full elimination of AST fallback seams from runtime |
| Unified bytecode VM | Accepted operator/property-access/control-flow/call subset; explicit no-mixed-execution rule | Broader call family coverage; optional chaining; deeper member chains |
| Expression VM | ExpressionProgram covers most expression shapes; inline expression buffers proven | Compact ExpressionOp storage as runtime contract rather than diagnostic tool |
| Standard Library | JsValue-native hot paths on most Array/String helpers; descriptor semantics proven | Full removal of object-overload compat tripwires |
| Async scheduling | Microtask queue ownership proven; await/resume contract explicit | Dedicated async-generator IR executor (Milestone C) |
| Module runtime | ESM lifecycle, registry, dynamic import phases, top-level await proven | Node.js-competitive CommonJS ergonomics (host-layer work) |
| Host interop | Host callable/global bridge boundaries explicit | Broader Node.js-style host behavior (integration-layer work) |

## Architecture constraints (current reality)

These constraints are binding until new evidence says otherwise.

- **Milestone A (module/runtime boundary):** ESM and async module behavior are proven owner surfaces; no full Node module/CommonJS parity claim.
- **Milestone B (host interop boundary):** host callable/global integration is explicit; Node-style host behavior remains an integration-layer concern.
- **Milestone C (async seam closure):** async-generator runtime still depends on the shared `ExecuteAsyncStep` bridge; this is active follow-through work.
- Unified bytecode production routing remains bounded by explicit eligibility and opcode/control-flow constraints until parity + profile evidence is added.
- Dynamic/eval-sensitive paths remain correctness-first; they cannot be erased by architecture preference.
- Compact statement-bytecode storage is directional; it is not the current universal execution contract.

## Delivery lifecycle

```mermaid
flowchart LR
    I[Architecture intent\ndream + roadmap constraint] --> B[Bounded design slice\none primary owner module]
    B --> C[Owner-surface implementation]
    C --> D[Focused semantics proof\nnarrow Test262 or unit pack]
    D --> E[Profile or benchmark proof\nbaseline + final signal]
    E --> F[Canonical quality gate\nmake quality green]
    F --> G[ADR and roadmap update]
    G --> H[Next bounded slice]

    D -. fail .-> B
    E -. fail .-> B
    F -. fail .-> B
```

Delivery invariants:
- One slice, one primary owner module. Cross-module edits require an explicit boundary contract.
- Node.js-competitive and CommonJS-adjacent wording must remain directional unless the slice carries explicit new proof.
- Async-generator seam follow-through stays packetized under Milestone C until the `ExecuteAsyncStep` bridge dependency is removed with focused proof.
- Unified-bytecode eligibility widening requires parity + profile proof; no silent boundary growth.

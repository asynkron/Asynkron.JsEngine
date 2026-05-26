# Unified Bytecode Prototypes

When extending the unified bytecode prototype, keep it IR-owned, internal, and
all-or-nothing until a separate routing issue proves production readiness.

## Rules

1. Use `ExecutionPlan` as the prototype compiler input. Do not create a
   parallel AST-to-unified-bytecode compiler for shapes that the current IR
   already lowers and annotates.
2. Keep eligibility at compile time, including function kind. Unsupported
   statement shapes, expression ops, identifiers, async/generator functions,
   local/declaration forms outside the exact accepted slice, control flow
   outside the accepted branch-plus-canonical-loop-back-edge slice, or dynamic
   shapes must return an unsupported reason before VM execution.
   Do not infer sync-only eligibility from `ExecutionPlan` shape alone.
3. Do not add fallback inside `UnifiedBytecodeVirtualMachine` to
   `ExpressionProgram` evaluation, AST evaluation, or `ExecutionPlanRunner`.
   VM execution should only execute bytecode the unified compiler emitted.
4. Do not route normal production execution through the unified VM in a
   prototype-expansion issue. Runtime routing needs its own issue and proof
   pack.
5. When expanding linear declaration/return packs, flatten only the supported
   `ExpressionProgram` operations into unified instructions. Identifier reads
   should become `LoadSlot`, literals should become program-owned
   `LoadLiteral` entries, and binary operators should stay limited to the
   explicitly proven numeric VM surface. Do not introduce an
   `EvalExpressionProgram` opcode or a runtime callback into the existing
   expression interpreter.
6. For each accepted shape, add focused tests for the emitted unified opcode
   stream, a minimal execution result, and at least one nearby unsupported
   shape that declines cleanly. When an accepted body shape can also appear in
   async or generator functions, include function-kind negative tests.
7. Keep JavaScript semantic claims narrow. A prototype op such as numeric
   `Add` proves only the tested VM behavior; full JavaScript operator coercion
   requires an explicit migration and parity proof.
8. When expanding across branch/control flow, keep accepted CFG ownership
   compiler-side and explicit. Branch shapes plus one canonical
   condition-first loop back-edge IR shape are accepted; all other
   loop/control-flow families must be rejected before VM execution. Compile with
   an IR-instruction-index to unified-bytecode-PC map, patch forward branch/jump
   operands after targets are emitted, and reject unsupported branch payloads or
   non-canonical loop shapes before execution. Do not treat this bounded loop
   support as broad loop support or as permission to call back into existing
   evaluators.

## Why

Issue #2118 / PR #2137 introduced the first unified bytecode slice for
`function add(x, y) { return x + y; }`. The useful decision was not just the
new files; it was the boundary: compile from existing `ExecutionPlan`, flatten
only the proven return-expression payload, keep the VM fallback-free, and leave
production routing untouched. Future agents should preserve that boundary so
unified-bytecode coverage gaps stay explicit instead of being masked by the
existing statement IR, expression bytecode, or AST evaluators.

Issue #2139 / PR #2144 expanded the prototype to an exact linear local
declaration plus return shape and then fixed review feedback by passing
function-kind metadata into `UnifiedBytecodeCompiler.TryCompile`. The lesson is
that async and generator bodies can look shape-compatible while requiring
promise, iterator, and suspension semantics that the current unified VM does
not implement. Function kind must stay part of the compile-time eligibility
contract.

Issue #2158 / PR #2162 expanded the same prototype into a small linear sync
expression pack with multiple declarations, literals, and numeric binary
operators. The lesson is that this expansion must still own the bytecode it
executes: literals belong in `UnifiedBytecodeProgram`, supported expression ops
are flattened into unified instructions, and unsupported statements or
expression ops decline before execution. Adding a generic expression-program
eval opcode would hide coverage gaps and make the fallback-free VM boundary
untrue.

Issue #2166 / PR #2173 crossed the prototype from linear body walking into
acyclic branch CFG compilation. The lesson is that branch support needs an
explicit bytecode-PC owner: map IR instruction indices to emitted PCs and patch
`JumpIfFalse` and `Jump` targets after blocks are emitted.

Issue #2182 / PR #2186 then extended that boundary to one canonical
condition-first back-edge IR shape (currently produced by guarded `while` and
equivalent condition-only `for` forms without initializer/post-update or loop
control statements). The lesson is to keep this loop support narrow and
compiler-owned: accept only the proven canonical IR topology, reject other
loop/control-flow families before unsupported details hide the real boundary,
and keep production routing unchanged. The review correction on #2182 is part
of the rule: this is not a source-syntax `while` exception; source forms are
eligible only when lowering produces the same condition-first back-edge shape.

Related ADRs:
- `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- `docs/adrs/0186-keep-unified-bytecode-function-kind-eligibility-explicit.md`
- `docs/adrs/0189-keep-unified-bytecode-linear-expression-packs-flattened.md`
- `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`

# Comparing to Jint

## Do Say
- “We need to profile and apply targeted optimizations.”
- “Let's profile to find the bottleneck.”
- “What are the remaining allocation hotspots?”

## Do NOT Say
- “Performance gap is due to deeper architectural differences.”
- “Bytecode compilation (like Jint's interpreter)” — Jint is not a bytecode interpreter.

## Key Facts
- Jint is an AST-walking interpreter (same style as ours).
- Jint uses `readonly struct ExecutionContext`; we use class `EvaluationContext` (needed for async/await).
- Always compare using profiling data, not vague architecture claims.

## Investigation Approach
1. Profile both engines on the same workload.
2. Compare hot function counts and time distribution.
3. Identify specific allocations/operations that differ.
4. Optimize each bottleneck.
5. Repeat until parity or trade-offs are clear.

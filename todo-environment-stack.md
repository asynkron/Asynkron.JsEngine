# Environment Stack Refactor

## Problem

Nested for-loops with for-await-of produce incorrect results (sum=6 instead of 12). The root cause is complex scope resolution logic that searches for bindings or scope IDs, which can find stale environments from previous outer loop iterations.

## Solution: Stack-Based Environment Model

Replace the current complex scope resolution with a simple stack model:

- `environment` always points to the current leaf (top of stack)
- `Enclosing` forms the stack chain
- Enter scope → push (PushEnvironmentInstruction)
- Exit scope → pop (PopEnvironmentInstruction)
- Never null out state until function exits

## Instructions

### PushEnvironmentInstruction
Creates a new environment and pushes it onto the stack:
- Creates env with parent = current env's Enclosing (for iteration) or current env (first iteration)
- For loop iterations: copies bindings from previous iteration
- Sets `environment = newEnv`

### PopEnvironmentInstruction
Pops the current environment:
- If current env's ScopeId matches, sets `environment = environment.Enclosing`
- Returns popped env to pool if allowed
- No-op if ScopeId doesn't match (scope was never entered)

### BreakInstruction / ContinueInstruction
Now include `TargetScopeId`:
- Pop environments until reaching TargetScopeId before jumping
- Handles multi-level breaks/continues that skip nested scopes

## Key Principles

1. **Per-iteration envs are siblings** - they share the same parent (loop scope), not chained to each other
2. **Parent is derived from chain position** - no scope ID lookups or binding searches needed
3. **Pop returns to pool** - when popping, return the popped env to pool if it was pooled
4. **Break/continue pop to target** - use TargetScopeId to unwind the right number of levels

## Nested Loop Flow Example

```
i=0: PUSH_ENV (scopeId=3), env = iterEnv_i0 (encloses body)
  j=0: PUSH_ENV (scopeId=5), env = iterEnv_j0 (encloses iterEnv_i0)
    for-await n: runs
  j=1: PUSH_ENV (scopeId=5), env = iterEnv_j1 (copies from j0, encloses iterEnv_i0)
    for-await n: runs
  exit j-loop: POP_ENV (scopeId=5), env = iterEnv_i0
i=1: PUSH_ENV (scopeId=3), env = iterEnv_i1 (copies from i0, encloses body)
  j=0: PUSH_ENV (scopeId=5), env = iterEnv_j0' (encloses iterEnv_i1)
    for-await n: runs
  ...
exit i-loop: POP_ENV (scopeId=3), env = body
```

## Implementation Status

- [x] Rename CreateIterationEnvironmentInstruction → PushEnvironmentInstruction
- [x] Rename PopIterationEnvironmentInstruction → PopEnvironmentInstruction
- [x] Add TargetScopeId to BreakInstruction with pop logic
- [x] Add TargetScopeId to ContinueInstruction with pop logic
- [x] Update ExecutionPlanBuilder to pass ScopeId to LoopScope
- [ ] Test with nested loop cases
- [ ] Consider ReturnInstruction popping all envs

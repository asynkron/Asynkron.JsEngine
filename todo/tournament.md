# Tournament: Evolutionary Agent Competition

A survival-of-the-fittest approach to fixing tests using parallel agents.

## Concept

Multiple agents work independently on the same problem. After a time limit:
- Agents are evaluated by a quantifiable metric (test pass count)
- The winner's branch becomes the base for the next round
- Losers are deleted
- Repeat until goal is achieved

```
Round 1: 4 agents branch from main
    │
    ├── Agent 1: tries approach A
    ├── Agent 2: tries approach B  ← WINNER (most tests fixed)
    ├── Agent 3: tries approach C
    └── Agent 4: tries approach D
    │
    ▼
Round 2: 4 agents branch from Agent 2's work
    │
    ├── Agent 5: refines approach B
    ├── Agent 6: tries B + variation  ← WINNER
    ├── Agent 7: tries B + different fix
    └── Agent 8: explores alternative
    │
    ▼
    ... continue until all tests pass
```

## Setup

### 1. Create `todo/todo.md` with:

```markdown
# Tournament Goal

Fix the following failing tests.

Strategy notes:
- Categorize by probable root cause
- Decide: fix many related tests (harder, higher reward) or easy wins (faster)?

After X time, you will be stopped and evaluated.
The winner gets to live on and multiply. Losers get deleted forever.

Document your findings at the bottom of this file.
These logs are your persistent memory across runs.

## Failing Tests

- TestCategory1
  - Test1.js
  - Test2.js
- TestCategory2
  - Test3.js

---
## Agent Insights (append here)

```

### 2. Ensure `todo.md` is committed

```bash
git add todo/todo.md
git commit -m "Tournament: round N goal"
```

## Running a Round

### Step 1: Create Worktrees

```bash
# From main (round 1)
git worktree add ../Asynkron.JsEngine-t1 -b tournament/r1-agent1 main
git worktree add ../Asynkron.JsEngine-t2 -b tournament/r1-agent2 main
git worktree add ../Asynkron.JsEngine-t3 -b tournament/r1-agent3 main
git worktree add ../Asynkron.JsEngine-t4 -b tournament/r1-agent4 main

# From a winner (round 2+)
git worktree add ../Asynkron.JsEngine-t1 -b tournament/r2-agent1 tournament/r1-agent2
# ... etc, branching from winner's branch
```

### Step 2: Cross-Link Worktrees

Before spawning agents, add links to ALL sibling worktrees in each todo.md:

```markdown
## Sibling Agents (check for progress to incorporate)

- ../Asynkron.JsEngine-t1/todo/todo.md
- ../Asynkron.JsEngine-t2/todo/todo.md
- ../Asynkron.JsEngine-t3/todo/todo.md
- ../Asynkron.JsEngine-t4/todo/todo.md
```

This allows agents to monitor each other's progress and cherry-pick independent fixes.

### Step 3: Spawn Agents

Ask Claude to spawn background agents:

```
Spawn 4 tournament agents in worktrees t1-t4.
Each should read todo/todo.md and work on fixing the failing tests.
Run them in background for 5 minutes.
```

### Step 3: Wait (~5 minutes)

Agents work in parallel. They:
- Read todo.md for the goal
- Categorize failures
- Make strategic decisions (easy wins vs big fixes)
- Document insights at bottom of todo.md

### Step 4: Evaluate

Tell Claude: "evaluate"

Claude will:
1. Run the test filter in each worktree
2. Count remaining failures
3. Report results table
4. Recommend winner or "resume all" if no progress

### Step 5: Decision

**If progress was made:**
```
Winner is Agent X (worktree t2).
Start round 2 branching from t2.
```

**If no progress:**
```
Resume all agents for another 5 minutes.
```

## Evaluation Command

Run this in each worktree to count failures:

```bash
cd ../Asynkron.JsEngine-tN
dotnet test tests/Asynkron.JsEngine.Tests.Test262 \
  --filter "FullyQualifiedName~TestName1|FullyQualifiedName~TestName2|..." \
  --no-build 2>&1 | grep -E "(Passed|Failed|Skipped)"
```

## Advancing to Next Round

```bash
# Delete loser worktrees
git worktree remove ../Asynkron.JsEngine-t1 --force
git worktree remove ../Asynkron.JsEngine-t3 --force
git worktree remove ../Asynkron.JsEngine-t4 --force

# Keep winner for reference or merge
# Option A: Branch new round from winner
git worktree add ../Asynkron.JsEngine-t1 -b tournament/r2-agent1 tournament/r1-agent2

# Option B: Merge winner to main and start fresh
git merge tournament/r1-agent2
git worktree add ../Asynkron.JsEngine-t1 -b tournament/r2-agent1 main
```

## Agent Memory

Agents document insights at the bottom of `todo.md`. This creates:
- **Persistent memory**: Findings survive across rounds
- **Evolutionary knowledge**: Winning insights propagate to offspring
- **Behavioral adaptation**: Agents can modify their own instructions

Example agent insights section:
```markdown
---
## Agent Insights

### Round 1, Agent 2 (WINNER)
- ModuleCode failures all related to missing `eval` in module scope
- The issue is in `ModuleEvaluator.cs:245` - doesn't set up eval binding
- Quick win: prefix-increment tests are simple operator precedence fix

### Round 2, Agent 3 (WINNER)
- Built on R1-Agent2's module fix
- Switch scope tests need lexical environment per case block
```

## Tips for Agents

1. **Categorize first**: Group failures by probable root cause
2. **Strategic choice**:
   - Many related tests = higher reward if fixed, but riskier
   - Easy isolated tests = guaranteed small progress
3. **Document everything**: Your insights help future generations
4. **Test frequently**: `dotnet test --filter "Name~X"` after each change
5. **Commit often**: Your branch is your survival

---

## Round History

### Round 1 (Initial)
- **Winner**: Agent 3
- **Fixed**: 6 switch tests (SwitchEmitter outer/inner block structure, hoisted let/const)
- **Key insight**: Switch statements need proper lexical scoping per case block

### Round 2
- **Winner**: Agent 4
- **Fixed**: 4 try/catch tests (TryEmitter improvements)
- **Key insight**: Try/catch completion value handling

### Round 3
- **Winner**: Agent 2
- **Fixed**: 6 switch scope tests (scope-lex-close-case, scope-lex-close-dflt, scope-lex-open-case)
- **Key insight**: Switch case blocks need separate lexical environments per ES spec 13.12.9

### Round 4
- **Winner**: Agent 3 (t3)
- **Fixed**: 8 tests total
  - Prefix ++/-- with valueOf (4 tests) - `TypedAstEvaluator.ExecutionPlanRunner.cs`
  - Catch block lexical scope (4 tests) - `TryEmitter.cs`
- **Key insights**:
  - `ToNumericValue` must pass context to properly handle ToPrimitive errors
  - Catch blocks need TWO environments per ES spec 14.15.2 (parameter + body)
- **Notable**: 3 agents (t1, t2, t3) independently fixed prefix ++/-- with same approach

### Round 5
- **Winner**: Agent 4 (t4)
- **Fixed**: 2 tests (completion-values-fn-finally-abrupt strict + non-strict)
- **File changed**: `TypedAstEvaluator.ExecutionPlanRunner.Completion.cs`
- **Fix**: Added `FinallyScheduled: false` check to catch handler condition
  ```csharp
  // BEFORE:
  if (kind == AbruptKind.Throw && frame is { HandlerIndex: >= 0, CatchUsed: false })

  // AFTER:
  if (kind == AbruptKind.Throw && frame is { HandlerIndex: >= 0, CatchUsed: false, FinallyScheduled: false })
  ```
- **Key insight**: Per ES spec, catch clause only handles exceptions from try block, NOT from finally. When `FinallyScheduled: true`, exceptions should propagate up, not go to catch.
- **Also analyzed**: TDZ bug in `function-local-closure-set-before-initialization.js` - complex issue where catch blocks have different scope requirements than try blocks
- **Notable**: t1 and t4 independently discovered same fix, t4 cherry-picked from t1

### Round 6
- **Winner**: Agent 2 (t2)
- **Fixed**: 25+ tests (all ModuleCode tests!)
- **File changed**: `JsEnvironment.cs` - `TryGetJsValueLocalSafe`
- **Fix**: Wrap `binding.JsValue` access in try-catch for import bindings
  ```csharp
  // Import bindings may throw if the source binding isn't defined yet
  try
  {
      value = binding.JsValue;
      return true;
  }
  catch (InvalidOperationException)
  {
      // Source binding not yet available
      value = default;
      return false;
  }
  ```
- **Key insight**: Import bindings referencing not-yet-initialized exports (e.g., `*default*` in self-importing modules) threw exceptions when `ScanEnvironmentForActiveIterators` tried to read them.
- **Tests fixed**:
  - All 24 ModuleCode tests with `*default*` binding issues
  - ModuleCode_topLevelAwait test (module-self-import-async-resolution-ticks.js)
- **Remaining (8 tests)**:
  - yield-star-from-try/catch (4): Complex generator iteration semantics
  - forOf using TDZ (2): `using` declaration not implemented
  - let TDZ closure (1): Complex scope issue
  - EvalCode (1): Delete behavior

---

## Cleanup

After tournament completes:

```bash
# Remove all tournament worktrees
git worktree list | grep tournament | awk '{print $1}' | xargs -I {} git worktree remove {} --force

# Delete tournament branches
git branch | grep tournament | xargs git branch -D

# Merge final winner to main
git checkout main
git merge tournament/rN-agentX
```

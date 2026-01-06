---
description: Generate hierarchical roadmap from GitHub issues
allowed-tools: Bash(gh:*)
model: haiku
---

# Roadmap Generator

Generate a hierarchical markdown roadmap from all GitHub issues and update/create the Roadmap issue.

## Process

### 1. Fetch all open issues

```bash
gh issue list --state open --limit 500 --json number,title,labels,body
```

### 2. Enforce naming conventions

Before generating the roadmap, ensure all issues follow consistent naming:

#### Naming Rules

| Label | Required Prefix | Example |
|-------|-----------------|---------|
| `epic` | `Epic: ` | `Epic: IR-only Execution` |
| `bug` | `Bug: ` | `Bug: strict mode block function scoping` |
| (tasks) | `Task X.Y: ` | `Task 0.1: Add AST-free assertion guard` |

#### Task Numbering Convention

- Top-level tasks under an epic: `Task 1:`, `Task 2:`, etc.
- Subtasks use dot notation: `Task 1.1:`, `Task 1.2:`, `Task 2.1:`, etc.
- Deeper nesting: `Task 1.1.1:`, `Task 1.1.2:`, etc.

#### Rename issues that don't follow conventions

For each issue that needs renaming:
```bash
gh issue edit <number> --title "<corrected title>"
```

Common renames needed:
- `Phase 0 —` → `Task 0:`
- `Phase 1 —` → `Task 1:`
- `P0.1:` → `Task 0.1:`
- `P1.2:` → `Task 1.2:`
- `bug:` (lowercase) → `Bug:`
- Missing `Epic:` prefix on epic-labeled issues

### 3. Categorize and organize issues

Parse each issue and categorize:

- **Bugs**: Issues with `bug` label (must have `Bug: ` prefix)
- **Epics**: Issues with `epic` label (must have `Epic: ` prefix)
- **Tasks**: All other issues (must have `Task X.Y: ` prefix)

For hierarchy, analyze:
- Task numbering patterns (e.g., `Task 0`, `Task 1.2`)
- Issue body for references to other issues (`Parent: #123`, `Blocked by #456`)
- Labels indicating parent/child relationships
- Semantic grouping by feature area or component

### 3.1 Analyze and consolidate related bugs

**Important**: Before listing bugs, analyze them to find related issues that share the same root cause.

1. **Read each bug's body** to understand:
   - The error message / exception type
   - The stack trace location
   - The test or scenario that triggers it
   - Any "Related" or "See also" references

2. **Identify common root causes** by looking for:
   - Same exception type (e.g., `TypeError: Cannot read properties of null`)
   - Same stack trace location (e.g., `TypedAstEvaluator.cs:line 598`)
   - Same feature area (e.g., "strict mode function scoping")
   - Explicit references like "same root cause as #X"

3. **Consolidate related bugs** hierarchically:
   - Pick the most descriptive bug as the parent (or the one that identifies the root cause)
   - List related bugs as nested bullets under it
   - Add a brief root cause description to the parent if known

Example consolidation:
```markdown
## Bugs

* #420 - Bug: strict mode block function scoping (root cause: null reference in FDI)
  * #421 - Bug: switch-case-decl-onlystrict.js
  * #423 - Bug: switch-dflt-decl-onlystrict.js
* #456 - Bug: unrelated standalone bug
```

4. **Update issue bodies** to cross-reference related bugs:
   - Add "Related: #X, #Y" to bug descriptions if not already present

### 4. Generate markdown structure

**Important formatting rules:**
- Only include issues that are **open** (exclude closed issues)
- Epics get `##` headers with the epic issue number
- Tasks are bullet points under epics
- Subtasks are nested bullet points (2-space indent) under their parent task
- This creates a proper visual hierarchy showing task relationships
- If a parent task is closed but has open subtasks, still show the parent as a header for context

```markdown
# Roadmap

## Bugs

* #123 - Bug: root cause issue (common error in XYZ)
  * #124 - Bug: related symptom 1
  * #125 - Bug: related symptom 2
* #456 - Bug: standalone unrelated bug

## Epic: Feature Name (#100)

* #101 - Task 0: Foundation
  * #111 - Task 0.1: Sub-task
  * #112 - Task 0.2: Another sub-task
* #102 - Task 1: Implementation
  * #113 - Task 1.1: Next task
  * #114 - Task 1.2: Another task
* #103 - Task 2: Standalone task (no subtasks)

## Epic: Another Feature (#200)

* #201 - Task 1: Related work

## Other

* #300 - Standalone task
```

### 5. Find or create Roadmap issue

Search for existing Roadmap issue:
```bash
gh issue list --search "Roadmap in:title" --state open --json number,title --limit 1
```

If exists, update it:
```bash
gh issue edit <number> --body "<generated markdown>"
```

If not exists, create it:
```bash
gh issue create --title "Roadmap" --body "<generated markdown>"
```

### 6. Report result

Output:
- Issues renamed (list each rename performed)
- Total issues processed
- Number of bugs found
- Number of epics found
- Number of tasks
- Link to the Roadmap issue

## Hierarchy Rules

1. **Only open issues are included** - closed issues are excluded from the roadmap
2. Task numbering indicates hierarchy depth (`Task 1.2` is under `Task 1`)
3. Issues referencing other issues (`Parent: #X`, `Blocked by #X`) are children
4. Epic labels indicate top-level groupings (must have `Epic:` prefix)
5. Bug labels go under the Bugs section (must have `Bug:` prefix)
6. Ungrouped issues go under "Other"

## Naming Migration Examples

| Current Title | Corrected Title |
|---------------|-----------------|
| `Phase 0 — Inventory + invariants` | `Task 0: Inventory + invariants` |
| `Phase 1 — Remove statement-level AST` | `Task 1: Remove statement-level AST` |
| `P0.1: Add AST-free assertion guard` | `Task 0.1: Add AST-free assertion guard` |
| `P1.2: IR support for for-in loops` | `Task 1.2: IR support for for-in loops` |
| `bug: strict mode block function scoping` | `Bug: strict mode block function scoping` |
| `IR-only Execution` (with epic label) | `Epic: IR-only Execution` |

## Reference Snapshot

This is the expected format for the current roadmap:

```markdown
# Roadmap

## Bugs

* #420 - Bug: strict mode block function scoping (TypeError: null ref in FDI)
  * #421 - Bug: switch-case-decl-onlystrict.js
  * #423 - Bug: switch-dflt-decl-onlystrict.js

## Epic: IR-only Execution (#364)

* #398 - Task 0: Inventory + invariants
  * #415 - Task 0.1: Add AST-free assertion guard
  * #416 - Task 0.2: Inventory of all StatementInstruction usages
* #399 - Task 1: Remove statement-level AST delegation
  * #404 - Task 1.1: IR support for block statements with lexical bindings
  * #405 - Task 1.2: IR support for for-in loops
  * #406 - Task 1.3: IR support for with statements (full coverage)
  * #410 - Task 1.7: IR support for yield in binding target defaults
* #400 - Task 2: Introduce expression bytecode
  * #411 - Task 2.1: Design expression bytecode format
  * #412 - Task 2.2: Expression bytecode emitter
  * #413 - Task 2.3: Expression bytecode interpreter
  * #414 - Task 2.4: Replace ExpressionNode operands with bytecode in IR instructions
* #401 - Task 3: Remove / quarantine AST evaluators
* #402 - Task 4: IL backend for sync bytecode
* #403 - Task 5: IL backend for generator/async stepping

## Epic: Refactor ExecutionPlanRunner (#365)

* #366 - Task 1: Extract ExecutionPlanRunner profiling helpers
* #368 - Task 2: Split instruction handlers into partial file
```

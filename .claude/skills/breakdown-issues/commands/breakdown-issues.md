---
allowed-tools: Bash(gh issue:*), Bash(gh label:*), Read, Grep, Glob, TodoWrite
description: Break down GitHub issues into smaller, actionable sub-tasks
---

# Issue Breakdown Skill

Break down GitHub issues recursively until all leaf tasks are either **simple** (actionable) or **research** (need investigation).

## Complexity Labels

The following complexity labels must exist in the repository:
- `simple` - Task completable in a focused coding session (1-2 hours)
- `moderate` - Task requires multiple steps but is well-understood
- `complex` - Task requires significant effort or has many sub-components
- `research` - Task needs investigation before it can be broken down

## Rules

1. **Stop conditions** - Stop breaking down when a task is:
   - Labeled `simple` (actionable in one session)
   - Labeled `research` (needs investigation first)
   - Already has 2+ child issues

2. **Keep breaking down** when a task is:
   - `moderate` or `complex` AND
   - Has 0-1 child issues

3. **No AI slop** - Sub-tasks must be:
   - Clear, actionable work items
   - Sum of children = parent functionality
   - Follow naming conventions (e.g., "Task 1.2.3: Do something")

4. **Hierarchical naming**:
   - Parent: "Task 1: Main work"
   - Children: "Task 1.1: Sub-work", "Task 1.2: Other sub-work"
   - Grandchildren: "Task 1.1.1: Detailed work"

5. **Mark as research** when:
   - Root cause is unknown
   - Multiple competing approaches with no clear winner
   - You lack context to make informed breakdown decisions

6. **CRITICAL: Trackable Relationships**:
   - Parent issues MUST have a task list with checkbox references to children
   - This creates a real GitHub relationship that tracks progress
   - When children are closed, the parent shows completion percentage
   - Just mentioning "Parent: #123" in the child body is NOT enough

## Process

### Step 1: Scan Open Issues
```bash
gh issue list --state open --limit 100 --json number,title,labels
```

### Step 2: Identify Issues Needing Breakdown
For each issue, check:
- Is it `moderate` or `complex`?
- Does it have 0-1 children?
- Is it NOT `simple` or `research`?

If all true → needs breakdown.

### Step 3: For Each Issue Needing Breakdown

1. **Read the issue** to understand scope:
   ```bash
   gh issue view <number>
   ```

2. **Assess if it can be broken down**:
   - Is the root cause/approach clear? → Break down
   - Is investigation needed first? → Mark as `research`

3. **Create sub-issues** (minimum 2 children):
   ```bash
   gh issue create --title "Task X.Y: Description" \
     --label "architecture,simple" \
     --body "Parent: #<parent_number>\n\n## Goal\n..."
   ```

4. **Update parent issue with task list** (CRITICAL for tracking):
   The parent MUST have a task list with checkboxes referencing children:
   ```markdown
   ## Sub-tasks
   - [ ] #101 - Task 1.1: First sub-task (simple)
   - [ ] #102 - Task 1.2: Second sub-task (simple)
   ```

   This creates a trackable relationship in GitHub:
   - Progress bar shows on parent issue
   - Closing children auto-updates the task list
   - Parent shows "2 of 5 tasks complete" etc.

   ```bash
   gh issue edit <parent_number> --body "## Sub-tasks
   - [ ] #<child1> - Task X.1: Description (complexity)
   - [ ] #<child2> - Task X.2: Description (complexity)
   ..."
   ```

5. **Recursively process** new sub-issues if they're `moderate` or `complex`

### Step 4: Verify Completion
All issues should now be one of:
- `simple` with 0 children (leaf task)
- `research` with 0 children (needs investigation)
- `moderate`/`complex` with 2+ children (properly decomposed)

## Sub-Issue Template

```markdown
Parent: #<parent_number>

## Goal
<One sentence describing what this task accomplishes>

## Scope
- <Bullet points of what's included>
- <Be specific about boundaries>

## Deliverables
- <Concrete outputs: files, functions, tests>

## Complexity: Simple|Moderate|Complex
<Brief justification for complexity rating>
```

## Example Breakdown

**Before:**
```
#100 Task: Implement caching (complex, 0 children)
```

**After:**
```
#100 Task: Implement caching (complex)
├── #101 Task: Design cache API (simple)
├── #102 Task: Implement cache storage (moderate)
│   ├── #103 Task: Memory cache backend (simple)
│   └── #104 Task: File cache backend (simple)
└── #105 Task: Add cache invalidation (simple)
```

## Execution Steps

1. Use TodoWrite to track progress through the breakdown
2. Process issues in batches (architecture tasks first, then bugs)
3. For each issue:
   - Read and understand the scope
   - Decide: break down OR mark as research
   - Create children with clear, actionable descriptions
   - Update parent to reference children
4. Recursively process any new moderate/complex children
5. Provide summary of work done

## Notes

- Bugs often don't need breakdown (single coherent fix)
- Mark bugs as `research` if root cause is unknown
- Epics are always `complex` by definition
- A sub-issue cannot be more complex than its parent

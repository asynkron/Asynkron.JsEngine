---
name: feedback_use_testrunner
description: Use /testrunner skill for scanning test results, not dotnet test loops. Work one todo file at a time.
type: feedback
---

Do NOT scan todo files by running `dotnet test` in a loop for each file. The /testrunner skill exists specifically for that purpose — it handles timeouts, hangs, and parallel execution properly.

**Workflow for fixing todo files:**
1. Pick one todo file
2. Run its tests (via testrunner or targeted dotnet test)
3. If any tests pass upfront, remove those entries from the file
4. Fix the remaining failures
5. Remove fixed entries / delete the file
6. Move on to the next file

**Why:** dotnet test hangs on some tests, can't handle timeouts properly, and running 456 sequential test invocations is slow and brittle. The testrunner was built to solve exactly this.

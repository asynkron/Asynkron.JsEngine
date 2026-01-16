---
active: true
iteration: 25
max_iterations: 0
completion_promise: null
started_at: "2026-01-15T21:20:39Z"
---

inside the ./todo folder, there are markdown files with a total of 15 000 failing tests. you will pick the first file, pick the first non closed test, search github issues if there are any hits for this specific faling test. read the relevant code, then ultrathink to come up with a plan how to solve it, try to solve it, run the specific test to verify, report any findings to github for this issue. if you fail to solve it, git revert and exit.. if you manage to solve it, run the internal testsuite, check off the item in the markdown file, git commit, exit. use all the tools you have, read @agents/how-to-debugging.md

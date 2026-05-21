# Tooling Shell Wrappers

Repository shell wrappers must preserve argument boundaries exactly.

## Argument Forwarding

- When copying Bash arrays, use `"${array[@]}"`, not `"${array[@]-}"`, inside
  another array assignment. The `-` form can expand an unset or empty array into
  one empty positional argument and silently change downstream CLI behavior.
- Add a focused empty-array proof whenever a wrapper fans out one parsed argument
  list into mode-specific invocations. The proof can be a small Bash snippet or
  an equivalent wrapper dry-run, but it must show that an empty source array
  produces zero forwarded tokens.
- Under `set -u`, do not expand optional Bash arrays directly into command
  invocations unless the empty case has been handled explicitly. Branch on
  `${#array[@]}` or use a proven zero-token expansion pattern so list, dry-run,
  and other smoke modes do not fail before the main execution path is reached.
- For wrappers that add default flags, prove both paths: defaults are injected
  when the caller omits the flag, and caller-supplied flags are preserved without
  an extra blank token.

## Why

Issue #1413 / PR #1422 fixed `tools/profile` after default profiling split mode
copied an empty `profiler_args` array with `"${profiler_args[@]-}"`. That
created a blank profiler argument, which broke the intended CPU/memory split
path even though each non-empty argument looked correctly quoted. Future shell
wrapper changes should treat empty argument lists as a first-class case, not as
an incidental detail of quoting.

Issue #1430 / PR #1437 hit the same class from the opposite direction: after the
local `asynkron-profiler` tool was bumped, review caught that `./tools/profile
list` failed because an empty `engine_args` array was expanded in a direct
runner invocation under `set -u`. The fix handled the empty list path explicitly
and used a zero-token expansion for Python fan-out arguments. Future wrapper
maintenance should smoke-test discovery/list paths as well as the main profiling
path, because those modes often run before callers have supplied any forwarded
arguments.

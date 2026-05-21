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

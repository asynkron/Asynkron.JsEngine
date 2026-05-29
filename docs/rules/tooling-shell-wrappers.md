# Tooling Shell Wrappers

Repository shell wrappers must preserve argument boundaries exactly.

## Argument Forwarding

- When copying Bash arrays, use `"${array[@]}"`, not `"${array[@]-}"`, inside
  another array assignment. The `-` form can expand an unset or empty array into
  one empty positional argument and silently change downstream CLI behavior.
- When invoking `rtk` from agent or docs examples, do not pass an entire
  compound shell snippet as one quoted executable argument, such as
  `rtk 'pwd; git status'`. `rtk` treats the first argument as the executable, so
  this fails as command-not-found and can be misdiagnosed as `rtk` being
  unavailable. Use separate `rtk <command>` invocations, or deliberately run a
  shell as the executable with `rtk /bin/sh -c 'pwd; git status'`.
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
- If a profile wrapper fails before the target runner starts because an optional
  Bash array is unset, isolate the target by running the underlying runner
  directly before judging the profile evidence. For Test262 manifest profiles,
  use `rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- <profile>`
  as the direct proof path, then record the wrapper failure separately instead
  of expanding a manifest-only slice into wrapper repair work.

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

Issue #2051 / PR #2058 accepted a focused Test262 manifest profile only after
the direct `ProfileRunner` command proved it, because `rtk ./tools/profile
test262-regexp-property-punctuation` failed in the wrapper with
`profiler_args[@]: unbound variable` before exercising the target profile. The
durable lesson is to separate wrapper preflight failures from profile validity:
direct-run the owning runner for evidence, and leave wrapper repair to a scoped
tooling change with empty-array proofs.

Issue `autrun-diubg9a7o0m0-624225cbc8` hit the agent-command variant during a
Dreamer evidence-only build pass: the first context read used `rtk` with a whole
multi-command shell snippet as one quoted argument, produced exit 127, and was
then reported as "`rtk` is unavailable" even though the wrapper was present. WHY:
future agents need to distinguish wrapper availability from command construction
errors, because misdiagnosing the wrapper can hide the repo's required command
contract and cause later evidence to be gathered through inconsistent shell
forms.

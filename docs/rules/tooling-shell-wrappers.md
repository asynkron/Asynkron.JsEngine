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

## Node.js vm.Script Execution Context

When measuring a JS profile script N times using Node.js `vm.Script` in a
**shared context**, wrap the script body in an IIFE before compiling:

```js
var code = '(function(){\n' + raw + '\n}());';
var s = new vm.Script(code);
```

Without the IIFE, top-level `let`, `const`, and `class` declarations throw a
redeclaration `SyntaxError` when the same `vm.Script` runs more than once
against the same `vm.createContext()`. The IIFE scopes each declaration
per-call, so the warmup and measured iterations all succeed.

WHY: issue #2706 / PR #2715 built the `check-nodejs-regression` gate. The IIFE
wrap is the key correctness fix for the multi-iteration shared-context design in
`measure_node_ms()`; without it any profile script that uses `let`/`const` at
the top level fails on the second run.

## Node.js Gate Entry Consistency

`tools/check-nodejs-regression` has a `gate_entries` array that controls which
profiles the gate measures. Whenever a new profile is added to
`tools/profile-manifest.json` **and** it should be tracked by the Node.js
throughput gate, all three surfaces must be updated together:

1. Add the profile script to `tools/profile-scripts/`.
2. Add a `"key:script_filename:iterations"` entry to `gate_entries` in
   `tools/check-nodejs-regression`.
3. Regenerate, stage, and commit the committed baseline with one pasteable
   command: `rtk ./tools/check-nodejs-regression --update && rtk git add tools/nodejs-baseline.json && rtk git commit`.

Omitting step 2 means the new profile is never measured or guarded even though
it appears in the manifest; omitting step 3 means the gate has no baseline to
compare against and will report `NO BASELINE` for the new profile.

WHY: issue #2706 / PR #2715 review caught that `fib-iterative` was added to
`profile-manifest.json` but omitted from `gate_entries` and the baseline JSON.
The two-line fix (add entry, run `--update`) was straightforward, but the review
cycle would have been avoided by treating all three steps as a single atomic
operation.

Issue #3127 / PR #3130 later repaired benchmark playbook review feedback where
Node.js baseline refresh instructions were split across separate update and git
commands. Keep future baseline-refresh guidance as one explicit update, stage,
and commit command so the regenerated baseline handoff is not interrupted
halfway through.

## Performance SLO Gate Consistency

`tools/check-slo-gate` has a `slo_profiles` array that controls which
profiles the timing SLO gate measures. Whenever a new profile should be guarded
by the SLO gate, all three surfaces must be updated together:

1. Add the profile script to `tools/profile-scripts/`.
2. Add the profile name to `slo_profiles` in `tools/check-slo-gate`.
3. Regenerate the committed baseline: `./tools/check-slo-gate --update`
   and commit `tools/perf-slo-baseline.md`.

Omitting step 2 means the new profile is never measured by the gate even though
it appears in the manifest; omitting step 3 means the gate has no baseline to
compare against and will report `NO BASELINE` for the new profile.

WHY: issue #2711 / PR #2716 introduced the timing SLO gate with the `startup`
and `microtask` profiles. The three-step atomic pattern mirrors the Node.js gate
entry consistency rule above and avoids the same class of "added to manifest but
not guarded" defect caught in issue #2706 / PR #2715.

Note: the SLO gate uses 200% tolerance by default (gate fails only when
measurement exceeds 3× the baseline) because CPU timing is hardware-dependent
and noisier than allocation bytes. The committed `tools/perf-slo-baseline.md` is
machine-specific; regenerate it with `--update` when switching developer hardware.

For same-run Node.js evidence in `tools/check-slo-gate`, match the lifecycle of
the Asynkron SLO profile being compared. Startup measures fresh engine/realm
initialization, so the Node reference must run each measured iteration in a
fresh `vm.Context` while still reusing the prepared `vm.Script`. Shared-context
reuse is appropriate only for workloads whose Asynkron profile also reuses the
same engine or whose target is steady-state execution. Keep the comparison
evidence non-failing unless a later ADR changes the SLO-gate contract.

WHY: issue #2927 / PR #2930 added p95 and same-run Node.js evidence to the SLO
gate, but the build-back commit `716c76e71` was needed because the first startup
comparison reused one Node context across iterations. That made Node measure
steady-state script execution while Asynkron measured fresh-engine startup,
making the ratio incomparable.

## Usage Heredoc Path Drift

When creating a new gate or wrapper script by mirroring an existing one, grep
for all path references — including those inside `usage()` heredocs and
`cat <<'USAGE'` blocks — and update them to match the new file locations.

Usage text is not executed and is easy to overlook during a template copy. A
stale path in a `usage()` heredoc misleads future maintainers about where the
committed baseline lives and makes the `--help` output incorrect.

WHY: issue #2711 / PR #2716 build fix corrected `.testrunner/perf-slo-baseline.md`
→ `tools/perf-slo-baseline.md` in the `check-slo-gate` `usage()` block. The
script was created by mirroring `check-allocation-regression`, which stores its
baseline at a different location; the usage heredoc was not updated to match the
new path.

# ADR Allocation

When creating a new ADR at any stage (learn, build, or knowledge-artifact work), reserve
the ADR ID from the host runtime first:

```bash
rtk faktorial-api adr-next
```

Use the wrapped command form in agent-runnable examples and handoffs. Plain
`faktorial-api adr-next` is only acceptable when prose is naming the helper, for
example when describing helper availability or failure evidence.

Use the returned `adr_id` as the four-digit filename prefix and heading number.
Do not derive the next ADR number by scanning `docs/adrs/*.md`, sorting filenames,
or guessing from nearby PRs.

After writing or renaming ADRs, run a duplicate-prefix check over `docs/adrs` and
record the clean result in the issue or stage summary.

Build-stage ADR numbers are provisional until the learn pass confirms them. Run
the duplicate-prefix check again after merging `main` into the branch, because a
sibling PR landing in that window can import a conflicting ADR prefix. When a
collision is found, renumber the newer ADR (keep the one already merged to `main`
stable) and update its heading plus every cross-reference.

## Why

Issue #1236 / PR #1242 repaired two accepted Temporal `ZonedDateTime` ADRs that
both used ADR number `0071`. The fix was only a docs rename and heading update,
but the duplicate number made references ambiguous and forced a separate cleanup
cycle. The runtime allocator is the source of truth for monotonic ADR IDs, while
filesystem scans can race with concurrent merged learn PRs.

Issue #2224 / PR #2238 found the ADR allocation guidance still mixed helper-name
prose with copy/paste command examples. In the agent runtime, shell commands
must follow the repo's `rtk` invocation contract, so future edits should keep
actionable allocator examples as `rtk faktorial-api adr-next` without rewriting
non-runnable helper-name references.

Issue #2668 / PR #2671 confirmed the duplicate-prefix check is not optional: the
runtime allocator handed back `adr_id 281`, but `0281-*` was already on disk —
consumed by a concurrent learn PR (the `bd70ca63` JsValue-native helpers work)
that merged between allocation and this run's write. The allocator is still the source of
truth for the *next* number, but a number it returns can already exist when a
sibling learn PR landed in the window. Remedy: after allocating, run the
duplicate-prefix check over `docs/adrs`, and if the returned prefix already
exists, re-allocate (the allocator increments per call) until you get a clean
one before writing the file. Do not fall back to scanning-and-incrementing as
the primary allocator.

Issue #2677 / PR #2682 extends the trigger beyond learn-stage writes. The
**build** stage authored the object-destructuring ADR and picked `0283` (the
allocator is framed for learn work, so build-stage authors tend to scan
`docs/adrs` instead). Meanwhile #2675's resumable this-binding ADR also took
`0283`, landed on `main`, and was pulled into this branch by a merge from `main`
between the build write and the learn pass — producing two distinct ADRs both
numbered `0283`. The learn pass detected it and renamed the object-destructuring
ADR to the free `0284` (heading + the contract/roadmap cross-references). Two
durable takeaways: (1) any stage that writes an ADR, including build, is exposed
to this collision, so treat a build-stage ADR number as **provisional** until
the learn pass confirms it; (2) the duplicate-prefix check must also run **after
merging `main`**, not only after allocation — a merge can import a sibling's ADR
number into your branch even when your number was clean when you wrote it. When
a collision is found, renumber the *newer* ADR (keep the one already merged to
`main` stable) and update its heading plus every cross-reference.

Issue #2679 / PR #2683 is a second occurrence of the exact #2677 pattern,
confirming it is systemic rather than a one-off. The build stage again authored
its labeled-control-flow ADR as `0283` by scanning instead of allocating;
meanwhile #2675's resumable this-binding ADR already held `0283` and #2677's
renumbered object-destructuring ADR held `0284`, both pulled in by a merge from
`main`. The learn pass detected the now triple-contended prefix and renamed this
PR's ADR to the free `0285` (heading + roadmap + the two contract
cross-references), leaving the #2675 `0283` async-generator references intact.
The takeaways from #2677 stand and are reinforced: build-stage ADR numbers are
provisional, the duplicate-prefix check must run after merging `main`, and the
*newer* ADR is the one to renumber. The standing fix is for ADR-authoring
stages (including build) to reserve via `rtk faktorial-api adr-next` rather than
scan `docs/adrs`, which is what keeps regenerating this collision.

Issue #2676 / spread-calls and #2678 / tdz-heads (PRs #2685 and #2687) are a third and fourth recurrence of the same pattern, now at ADR numbers 0285 and 0286. The build stage for gh2676 authored the spread-calls ADR as 0285 by scanning; meanwhile gh2679's labeled-control-flow ADR already held 0285 and was merged to main before gh2676's learn. Similarly, the build stage for gh2678 authored the TDZ-heads ADR as 0286 by scanning; meanwhile gh2690's construct-calls ADR already held 0286 and was merged via its learn PR (#2699) before gh2678's delivery PR (#2687) landed. The learn pass for gh2678 detected both collisions and renamed the newer files: spread-calls to 0287, TDZ-heads to 0288, then updated roadmap, expansion contract, and rule cross-references in the same slice.

Issues autrun-diwap9usj808 / gh2771 / gh2770 (PRs #2775, #2777, #2772) produced a **triple collision** at ADR prefix `0294` — the most severe collision to date. Three separate tasks each called the allocator (or scanned) and wrote a distinct `0294-*` file. The gh2771 learn pass (#2779) failed to detect the collision, allowing the problem to compound when gh2770's delivery (#2772) added a third file. The gh2770 learn pass (this entry) detected all three and renamed the two newer files: the optional-member-access ADR (gh2771) to `0296`, and the ternary-expression ADR (gh2770) to `0297`. Updated cross-references in `docs/roadmap.md`, `docs/unified-bytecode-expansion-contract.md`, and `docs/rules/unified-bytecode-prototypes.md`. The durable takeaways are: (1) the duplicate-prefix check is **mandatory at every learn pass**, not optional; a learn pass that skips it can allow a collision to remain and accumulate further conflicts; (2) build-stage ADR numbers remain provisional until the learn pass runs the check — do not skip the duplicate-prefix check even if allocation used `rtk faktorial-api adr-next`; (3) when the allocator is unavailable (HTTP API unreachable), scanning for the next free prefix is acceptable **for collision repair only** — do not use scanning as the primary allocator for new ADRs.

Issue #gh2809 / PR #2811 is a further recurrence of the collision pattern at ADR prefix `0290`. The iterator-result ADR (`0290-reduce-iterator-result-allocation-resumable-generator.md`) was merged after the array/object-literals ADR (gh2705) had already claimed `0290`. The build agent detected the collision, attempted `rtk faktorial-api adr-next` (which returned HTTP 404 — allocator unavailable), and correctly fell back to scanning under the **collision-repair exception**: scanned `docs/adrs` for the highest used prefix (0298), used 0299 as the next free number, renamed the iterator-result file, updated its heading, and updated the one cross-reference in `docs/rules/generator-execution-path-parity.md`. This confirms the collision-repair scanning exception is exercised correctly when the allocator is unreachable. The broader pattern (new ADR written at build time without allocating, collision detected at learn time) recurs unchanged — reinforcing that the only robust fix is for all ADR-authoring stages to use `rtk faktorial-api adr-next` as the primary allocator.

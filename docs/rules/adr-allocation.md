# ADR Allocation

When creating a new ADR from Faktorial learn or knowledge-artifact work, reserve
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

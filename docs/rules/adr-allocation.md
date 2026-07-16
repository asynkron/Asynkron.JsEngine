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

The later incidents establish one shared contract. Every ADR-authoring stage
must allocate through `rtk faktorial-api adr-next`, then check for duplicate
prefixes immediately and again after merging `main`; the learn pass must never
skip that check. Keep an ADR already merged to `main` stable, rename the newer
ADR, and update its filename, heading, and every cross-reference together. A
filesystem scan is acceptable only to repair a collision when the allocator is
unavailable, never as primary allocation. If an allocated prefix is already on
disk, re-allocate until the prefix is clean before writing.

Collision chronology:

- Issue #2668 / PR #2671: the allocator returned `adr_id 281` after a concurrent
  learn PR for the `bd70ca63` JsValue-native helpers merged in the allocation/write
  window and consumed `0281`. Allocation and the immediate check are both required.
- Issue #2677 / PR #2682: build scanned and chose `0283`; a later `main` merge
  imported #2675's different `0283` before learn, which renamed the newer
  object-destructuring ADR to `0284` and updated its heading plus contract and
  roadmap references.
- Issue #2679 / PR #2683: build again scanned `0283`, while #2675 held `0283` and
  #2677 held `0284` after `main` was merged. Learn resolved the triple contention
  by moving the newer labeled-control-flow ADR to `0285`, updating its heading,
  roadmap, and two contract references while leaving #2675 references intact.
- Issues #2676 / spread-calls and #2678 / tdz-heads (PRs #2685 and #2687): gh2676
  scanned `0285` already held by merged gh2679; gh2678 scanned `0286` already held
  by gh2690, which merged via learn PR #2699 before delivery PR #2687. Learn renamed
  the newer files to `0287` and `0288`, then updated roadmap, expansion-contract,
  and rule references.
- Tasks autrun-diwap9usj808 / gh2771 / gh2770 (PRs #2775, #2777, #2772): three
  allocator-or-scan paths produced the most severe collision, three distinct
  `0294` ADRs. The gh2771 learn pass (#2779) missed it before #2772 added the third;
  gh2770 learn found all three, moved gh2771 optional-member-access to `0296` and
  gh2770 ternary to `0297`, and updated `docs/roadmap.md`,
  `docs/unified-bytecode-expansion-contract.md`, and
  `docs/rules/unified-bytecode-prototypes.md`.
- Task gh2809 / PR #2811: the iterator-result ADR collided at `0290` with gh2705's
  already-merged array/object-literals ADR. Build detected it; the allocator
  returned HTTP 404, so collision repair scanned a highest prefix of `0298`, moved
  the newer ADR to `0299`, and updated its heading and the reference in
  `docs/rules/generator-execution-path-parity.md`.
- Task gh2828 / PR #2832: optional-call-chain ADR `0299` collided with the
  already-merged iterator-result ADR. With the allocator unavailable (`No such
  file or directory`) and `0300` occupied, learn kept the merged ADR stable,
  moved the newer ADR to `0301`, and updated its filename, heading, roadmap, and
  rule cross-references.

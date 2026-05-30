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

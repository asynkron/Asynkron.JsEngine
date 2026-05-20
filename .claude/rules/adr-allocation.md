# ADR Allocation

When creating a new ADR from Faktorial learn or knowledge-artifact work, reserve
the ADR ID from the host runtime first:

```bash
faktorial-api adr-next
```

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

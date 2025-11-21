# Generator IR Limitations

This document tracks which generator constructs are currently hosted on the IR path, and which ones still fall back or are rejected explicitly.

## Supported
- `yield` in statements/expressions after lowering via `GeneratorYieldLowerer` (assignments, declarations, returns, conditions, `for` conditions/increments including two simple yields).
- `switch` statements with yield-free discriminant/tests, a single optional default in any position, and top-level `break;` statements (unlabeled or labeled to the switch label). The first break in a case ends that case; subsequent statements are ignored.
- `for...of` (sync/async) with simple bindings, `try/catch/finally`, `yield*`, and pending-completion plumbing for `.next/.throw/.return`.

## Still Unsupported / Explicitly Rejected
- Labeled `break` targeting outer labels (other than the current switch label).
- Multiple `break` statements whose control-flow would require executing statements after the first break.
- `switch` case bodies containing nested `try/finally` that must unwind across `break`/fallthrough.
- Any yield placement not normalized by `GeneratorYieldLowerer` (e.g., delegated yields in conditions, nested yields inside yielded expressions).

## Notes
- The replay-based generator runner has been removed; unsupported shapes now throw `NotSupportedException` with a reason.
- The lowerer assigns resume temps with the `__yield_lower_*` prefix; the IR builder treats assignments to these temps as yield points.

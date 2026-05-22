# Test262 Runner Notes

This test project runs a curated subset of ECMAScript Test262 failures/regressions against `Asynkron.JsEngine`.

## Run Full Regression Filter

From repo root:

```bash
rtk ./tools/run-test262-regressions.sh
```

This uses `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt` and executes:

```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "..."
```

## Run Named Regression Packs

List available packs:

```bash
rtk ./tools/run-test262-regressions.sh --list
```

Run a named pack:

```bash
rtk ./tools/run-test262-regressions.sh temporal
```

Named packs are resolved from:

- `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/<name>.filter.txt`

You can also pass an explicit path to a `.txt` filter file.

## Filter File Format

Filter files are plain text with one xUnit filter expression per line.

- Empty lines are ignored.
- Lines starting with `#` are ignored.
- Remaining lines are joined with `|` and passed to `dotnet test --filter`.

## Local Test262 Data Path

The runsettings files in this project only select test classes and do not set
the Test262 checkout path. `Test262SuiteDiskCache` controls loading and cache behavior.

Disk-cache environment variables:

- `JSENGINE_TEST262_DISK_CACHE` - enable/disable local disk cache (enabled by default).
  Set `0`, `false`, or `off` to disable disk cache and always fetch from GitHub.
- `JSENGINE_TEST262_DISK_CACHE_DIR` - optional base directory for extracted suites.
  The effective suite folder is `<base>/<sha>`.

Default cache location when `JSENGINE_TEST262_DISK_CACHE_DIR` is not set:

- `~/.asynkron/jsengine/test262/<sha>`

Example:

```bash
JSENGINE_TEST262_DISK_CACHE=1 \
JSENGINE_TEST262_DISK_CACHE_DIR=/tmp/jsengine-test262-cache \
  rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings
```

## Quality Gate Scope

`make quality` is the canonical fast quality gate and does not run Test262.

Test262 regression runs are separate, heavier proof commands. Run them explicitly with:

```bash
rtk ./tools/run-test262-regressions.sh
```

## Maintenance Workflow

When a recurring-child task asks for Test262 maintenance:

1. Reproduce with the smallest relevant filter pack.
2. Apply the narrowest fix in the owning engine slice.
3. Re-run the focused pack.
4. Re-run broader regression packs only as confirmation.
5. Update this README if local runner workflow or pack behavior changes.

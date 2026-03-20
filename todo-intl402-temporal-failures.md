# Investigation Report: ~300 Failing intl402/Temporal Test262 Tests

## Problem Summary
Approximately 300 intl402/Temporal Test262 tests fail due to 7 distinct root causes spanning the Temporal and Intl implementations. The failures cluster into: non-ISO calendar rejection, stub toLocaleString methods, missing Temporal type support in DateTimeFormat.format, timezone canonicalization gaps, incorrect DST day-length calculations, missing Locale features, and timezone alias resolution failures.

## Affected Components
- `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHelper.cs` — calendar validation, toLocaleString stubs, timezone canonicalization, DST calculations
- `src/Asynkron.JsEngine/JsTypes/JsTemporalZonedDateTime.cs` — timezone equality comparison
- `src/Asynkron.JsEngine/StdLib/Intl/IntlDateTimeFormatPrototype.cs` — ToEpochMilliseconds, timezone resolution
- `src/Asynkron.JsEngine/StdLib/Intl/IntlUtilities.cs` — TryNormalizeCalendar, TryCanonicalizeTimeZone, CanonicalizeTimeZoneId
- `src/Asynkron.JsEngine/StdLib/Intl/IntlLocaleConstructor.cs` — calendar validation, option ordering

## Evidence Collected

### Test Execution Results

Ran all affected test categories. Errors classify into these patterns:

**Pattern 1: Non-ISO calendar names rejected (~40 tests)**
```
'RangeError': 'Invalid calendar string: gregory'
'RangeError': 'Invalid calendar string: hebrew'
'RangeError': 'Invalid calendar string: buddhist'
'RangeError': 'Unsupported calendar: islamicc'
```
Affects: PlainYearMonth/from, PlainMonthDay/from, PlainDate/from, ZonedDateTime/from, Duration/total (relativeTo), Duration/round (relativeTo)

**Pattern 2: toLocaleString returns ISO string instead of locale-formatted string (~60 tests)**
```
Expected SameValue(«"2000-05-02"», «"2000-05-02[u-ca=gregory]"») to be true
Expected SameValue(«"2024-12-26T11:46:40.321Z"», «"2024"») to be true
PlainTime formatted with no options 11:46:40.321 should not include fractional second digits
```
Affects: All 7 Temporal type toLocaleString methods (Instant, PlainDate, PlainTime, PlainDateTime, PlainMonthDay, PlainYearMonth, ZonedDateTime)

**Pattern 3: Temporal.*.valueOf throws when format() tries numeric coercion (~15 tests)**
```
'TypeError': 'Temporal.Instant.prototype.valueOf does not support implicit conversion'
'TypeError': 'Temporal.PlainDate.prototype.valueOf does not support implicit conversion'
'TypeError': 'Temporal.PlainTime.prototype.valueOf does not support implicit conversion'
```
Affects: Tests that call `Intl.DateTimeFormat.format(temporalObject)` or `defaultFormatter.format(date)`

**Pattern 4: Timezone canonicalization failures (~25 tests)**
```
Expected SameValue(«"Africa/CAIRO"», «"Africa/Cairo"») to be true
Time zone Etc/GMT should be equal to primary identifier UTC
'RangeError': 'Unsupported timeZone 'Australia/Canberra''
'RangeError': 'Unsupported timeZone 'Etc/GMT''
```
Affects: ZonedDateTime/from, ZonedDateTime/equals, DateTimeFormat (canonicalize-timezone, canonicalize-utc-timezone)

**Pattern 5: DST day-length not accounted for (~20 tests)**
```
start inside repeated hour, end after: 25 hours = 1 day Expected SameValue(«1.0416666666666667», «1») to be true
24 hours does not balance to 1 day in 25-hour day Expected SameValue(«367.0416666666667», «366.96») to be true
1 month 15 days 11:30 should be exactly 1.5 months Expected SameValue(«1.4993279569892473», «1.5») to be true
```
Affects: Duration/total, Duration/round, ZonedDateTime/since (DST tests), ZonedDateTime/until (DST tests)

**Pattern 6: getTimeZoneTransition wrong values (~12 tests)**
```
DST transition minus one nanosecond Expected SameValue(«"2021-03-28T03:00:00+02:00[Europe/Berlin]"», «"2020-10-25T02:00:00+01:00[Europe/Berlin]"»)
Expected SameValue(«1572764399000000000n», «1572760800000000000n») to be true
```
Affects: ZonedDateTime/getTimeZoneTransition (all sub-tests)

**Pattern 7: Intl.Locale issues (~29 tests)**
```
'RangeError': 'Invalid Intl.Locale calendar option'  (rejects valid BCP47 subtags like "abc")
new Intl.Locale("es-ES-preeuro").minimize().toString() returns "es-preeuro" Expected SameValue(«"es-Latn-ES-preeuro-preeuro"»)
Intl.Locale() throws TypeError Expected a TypeError but got a RangeError
constructor-getter-order: missing "get variants"/"toString variants" in property access sequence
DateTimeFormat returns invalid locale sv-SE
```

### Code Analysis

#### Root Cause 1: `ResolveTemporalCalendarId` rejects non-ISO calendar names
**File:** `TemporalHelper.cs:9016-9074`

The function `ResolveTemporalCalendarId` (line 9016) only recognizes "iso8601" as a direct calendar ID (line 9049). When a property bag contains `calendar: "gregory"` or `calendar: "hebrew"`, the function falls through to try parsing it as an ISO date string, which fails at line 9074 with "Invalid calendar string: {name}".

There is a correct `ValidateCalendarId` function at line 223 that properly uses `ValidCalendarIds` (a HashSet containing all 18 valid calendar IDs) and `CalendarAliases` (mapping "islamicc" to "islamic-civil"), but `ResolveTemporalCalendarId` doesn't use it.

The critical code path (line 9048-9074):
```csharp
// Only handles iso8601:
if (string.Equals(calStr, "iso8601", StringComparison.OrdinalIgnoreCase))
    return "iso8601";
// Falls through to ISO string parsing, then throws:
throw StandardLibrary.ThrowRangeError($"Invalid calendar string: {calStr}", realm: realm);
```

**Fix:** After the iso8601 check, add: lowercase the string, check `CalendarAliases` for alias resolution, then check `ValidCalendarIds`.

#### Root Cause 2: All Temporal `toLocaleString` methods are stubs
**Files:** `TemporalHelper.cs` lines 767, 1514, 1966, 2312, 2829, 3468, 3781

Every Temporal type's `toLocaleString` ignores the `locales` and `options` arguments and just returns the ISO string from `toString()`. Per spec, `toLocaleString` should create an `Intl.DateTimeFormat` with the given locale/options and format the Temporal value.

Example (Instant, line 767):
```csharp
AddPrototypeMethod(prototype, realm, "toLocaleString", 0, (thisValue, _) =>
{
    var instant = GetInstant(thisValue);
    return new JsValue(instant.ToString()); // BUG: ignores locale/options
});
```

The method signature also has `0` for the length parameter but should be `0` for most (locale is optional), yet it discards `args` entirely via `_`.

**Fix:** Each toLocaleString needs to: (1) accept `(locales, options)` arguments, (2) create an `Intl.DateTimeFormat` with appropriate defaults for each Temporal type, (3) format the Temporal value through it.

#### Root Cause 3: `DateTimeFormat.format()` doesn't support Temporal types
**File:** `IntlDateTimeFormatPrototype.cs:1651-1677`

`ToEpochMilliseconds` (line 1651) converts the input to a number via `JsOps.ToNumber(value)`, which calls `valueOf()`. Temporal types throw TypeError on `valueOf()`. The function has no Temporal type detection at all.

```csharp
private static double ToEpochMilliseconds(JsValue value)
{
    // ... checks for null/undefined and _internalDate ...
    var number = JsOps.ToNumber(value); // Calls valueOf() -> throws for Temporal types
    return TimeClip(number);
}
```

**Fix:** Before the `ToNumber` fallback, check for Temporal internal slots:
- `Temporal.Instant` -> extract epoch milliseconds directly
- `Temporal.PlainDate` -> create midnight datetime, get epoch ms (requires timezone assumption)
- `Temporal.PlainDateTime` -> get epoch ms
- `Temporal.PlainTime` -> extract time components
- `Temporal.ZonedDateTime` -> use instant's epoch ms
- `Temporal.PlainYearMonth`, `Temporal.PlainMonthDay` -> not directly formattable (spec throws TypeError)

#### Root Cause 4: Timezone canonicalization gaps
**Multiple files:**

**4a. `JsTemporalZonedDateTime.Equals` uses string comparison (line 821):**
```csharp
return Instant.Equals(other.Instant) && string.Equals(TimeZoneId, other.TimeZoneId, StringComparison.Ordinal);
```
Per spec, `TimeZoneEquals` should resolve both timezone IDs to their primary identifiers via `GetAvailableNamedTimeZoneIdentifier` before comparing. So "Etc/GMT" and "UTC" should be equal (same primary identifier "UTC"), but ordinal string comparison makes them unequal.

**4b. `CanonicalizeTimeZoneId` in TemporalHelper.cs (line 10357):**
Only handles "UTC" specially. For IANA names, it calls `FindSystemTimeZoneById` and returns `.Id`. On macOS with case-sensitive IANA lookup, "Africa/CAIRO" may fail or return the un-canonicalized ID.

**4c. `IntlUtilities.CanonicalizeTimeZoneId` (line 512):**
Extremely basic — only handles "UTC" and replaces spaces with underscores. Doesn't resolve IANA aliases (e.g., "Australia/Canberra" -> "Australia/Sydney", "Etc/GMT" -> "UTC").

**4d. `IntlUtilities.TryCanonicalizeTimeZone` (line 388):**
Uses a registry built from `TimeZoneInfo.GetSystemTimeZones()`. The registry lookup is case-sensitive and doesn't include all IANA aliases. "Etc/GMT" and "Australia/Canberra" may not be in the registry's lookup table.

**Fix:** Need a comprehensive IANA timezone alias/canonicalization table, or use .NET's `TimeZoneInfo.TryConvertIanaIdToWindowsId`/`TryFindSystemTimeZoneById` more aggressively with case-insensitive matching.

#### Root Cause 5: Duration.total ignores DST day-length with ZonedDateTime relativeTo
**File:** `TemporalHelper.cs:7395-7418`

When a `ZonedDateTime` relativeTo is provided but the duration has no calendar units (years/months/weeks=0) and the target unit is day-or-smaller, the code takes a shortcut (line 7407-7416) that uses `DurationToTotalNanoseconds` with a fixed 24-hour day assumption:

```csharp
if (duration.Years == 0 && duration.Months == 0 && duration.Weeks == 0 &&
    unitRank <= TemporalUnit.Day)
{
    ValidateZonedDateTimeAdd(zonedDateTimeRelativeTo, duration, realm);
    var totalNs = DurationToTotalNanoseconds(duration.Days, ...);
    var unitNs = new BigInteger(GetUnitNanoseconds(unit));
    return DivideToDouble(totalNs, unitNs);
}
```

`GetUnitNanoseconds("day")` returns `86_400_000_000_000` (24 hours), but DST days can be 23 or 25 hours. The spec requires computing the actual day length from the ZonedDateTime's timezone, adding the duration to the ZonedDateTime to find the endpoint, and using the actual nanosecond difference.

**Fix:** When unit is "days", must compute the actual day length at the `relativeTo` point by finding the epoch nanoseconds at `relativeTo + 1day` minus `relativeTo`, using the timezone's DST rules.

#### Root Cause 6: getTimeZoneTransition uses imprecise .NET adjustment rules
**File:** `TemporalHelper.cs:3099-3247`

The implementation scans `TimeZoneInfo.GetAdjustmentRules()` to find transitions, but:
1. Truncates to millisecond precision (line 3164: `epochMs = (long)(epochNs / 1_000_000)`) — loses nanosecond precision
2. The `GetTransitionPoint` helper may not compute exact UTC transition times correctly
3. `GetAdjustmentRules()` on macOS uses different internal data than IANA tzdb, leading to slightly different transition times
4. The direction search logic doesn't handle multi-year rules correctly (lines 3179, 3187)

The test expects nanosecond-precise transition times matching IANA tzdb data.

#### Root Cause 7: Intl.Locale validation too strict + missing features
**File:** `IntlLocaleConstructor.cs:114-122`, `IntlUtilities.cs:237-241`

**7a. Calendar validation:** `TryNormalizeCalendar` only accepts calendars in `CalendarSet` (18 known calendars). Per spec, any valid Unicode type subtag (`alphanum{3,8}(-alphanum{3,8})*`) should be accepted. A value like "abc" or "1234abcd" is valid BCP 47 but rejected.

**7b. caseFirst validation:** The test `constructor-options-casefirst-invalid.js` expects `caseFirst: "Upper"` to throw RangeError (case-sensitive), but the engine accepts it (no case-sensitive check).

**7c. Locale constructor without new:** `Intl.Locale()` (without `new`) should throw TypeError, but throws RangeError instead.

**7d. Getter ordering:** The constructor getter order test expects `variants` between `region` and `calendar`, and `numberingSystem` after `numeric`. The engine's order has `numberingSystem` after `calendar` and omits `variants` from the construction-time property access sequence.

**7e. Locale minimize/maximize:** `"es-ES-preeuro"` minimized should be `"es-preeuro"` but produces `"es-Latn-ES-preeuro-preeuro"`, indicating both likely subtags lookup and variant handling have bugs.

**7f. DateTimeFormat locale output:** `resolvedOptions().locale` returns `sv-SE` but the test's `isCanonicalizedStructurallyValidLanguageTag` rejects it. This may be a locale canonicalization issue (BCP 47 requires region subtags to be canonical).

## Root Cause Analysis

### Root Cause 1 (Highest Impact — ~40 tests): Non-ISO calendar rejection in ResolveTemporalCalendarId
**TemporalHelper.cs:9048-9074** — The `ResolveTemporalCalendarId` function only recognizes "iso8601" as a direct calendar ID. All other valid calendar names ("gregory", "hebrew", "buddhist", etc.) are rejected with "Invalid calendar string". A correct `ValidateCalendarId` function exists at line 223 but isn't called.

- Evidence supporting: Every test using `calendar: "gregory"` (or any non-iso8601) in a property bag fails with this exact error
- Confidence: **High** — direct code path analysis confirms the bug

### Root Cause 2 (High Impact — ~60 tests): Stub toLocaleString + missing Temporal support in DateTimeFormat
**TemporalHelper.cs** (all toLocaleString methods) + **IntlDateTimeFormatPrototype.cs:1651** — Two related issues: (a) toLocaleString on all Temporal types is a stub returning `toString()`, and (b) `DateTimeFormat.format()` calls `valueOf()` on inputs, which Temporal types reject.

- Evidence supporting: All toLocaleString tests fail, either with wrong string format or valueOf TypeError
- Confidence: **High** — code explicitly returns `ToString()` and ignores arguments

### Root Cause 3 (High Impact — ~25 tests): Timezone alias/canonicalization
**Multiple locations** — No comprehensive IANA timezone alias resolution. "Etc/GMT", "Etc/UTC", "GMT" aren't mapped to "UTC" primary identifier. "Australia/Canberra" isn't resolved to "Australia/Sydney". Case-insensitive lookup incomplete.

- Evidence supporting: Tests fail with exact timezone name comparison errors
- Confidence: **High** — `CanonicalizeTimeZoneId` code is clearly minimal

### Root Cause 4 (Medium Impact — ~20 tests): DST day-length calculation
**TemporalHelper.cs:7407-7416** — Duration.total/round with ZonedDateTime relativeTo uses fixed 24-hour day assumption instead of actual DST-adjusted day length.

- Evidence supporting: All DST-related Duration tests produce values consistent with 24h day (e.g., 25/24 = 1.0417 instead of 1.0)
- Confidence: **High** — the calculation path clearly uses `GetUnitNanoseconds("day")` = 86.4T ns

### Root Cause 5 (Medium Impact — ~12 tests): getTimeZoneTransition precision
**TemporalHelper.cs:3099-3247** — Uses .NET adjustment rules with millisecond-truncated precision, producing transition times that don't match IANA tzdb.

- Evidence supporting: Close but wrong transition nanosecond values
- Confidence: **Medium** — some failures could also be due to different tzdb versions

### Root Cause 6 (Medium Impact — ~29 tests): Intl.Locale validation/features
**IntlLocaleConstructor.cs, IntlUtilities.cs** — Multiple smaller issues: overly strict calendar validation, wrong error types, incorrect getter order, broken minimize/maximize for variant subtags.

- Evidence supporting: Multiple distinct error patterns each trace to specific code
- Confidence: **High** for individual issues, **Medium** for completeness of diagnosis

## Recommended Fixes (Ordered by Impact)

### Fix 1: Calendar recognition in ResolveTemporalCalendarId (~40 tests)
**File:** `TemporalHelper.cs:9048`
After the `iso8601` check, add:
```csharp
var lowered = AsciiLowercase(calStr);
if (CalendarAliases.TryGetValue(lowered, out var canonical))
    lowered = canonical;
if (ValidCalendarIds.Contains(lowered))
    return lowered;
```
Then fall through to ISO string parsing only if not a known calendar.

**Estimated fix: ~5 lines changed. Impact: ~40 tests fixed.**

### Fix 2: Temporal toLocaleString + DateTimeFormat Temporal support (~60 tests)
Two parts:
1. **toLocaleString**: Accept `(locales, options)` args, create `Intl.DateTimeFormat` instance, and delegate formatting. Each Temporal type needs its own default options (PlainDate: no time; PlainTime: no date; ZonedDateTime: date+time+timeZoneName; etc.)
2. **ToEpochMilliseconds**: Before `ToNumber`, detect Temporal types and extract epoch ms:
   - Instant: `instant.EpochMilliseconds`
   - ZonedDateTime: `zdt.Instant.EpochMilliseconds`
   - PlainDate/PlainDateTime: compute epoch ms from date components

**Estimated fix: ~150 lines. Impact: ~60 tests fixed.**

### Fix 3: Timezone canonicalization (~25 tests)
Need a timezone alias table or improved .NET interop:
1. Map UTC-equivalent zones: "Etc/GMT", "Etc/UTC", "GMT" -> primary identifier "UTC"
2. Map IANA aliases: "Australia/Canberra" -> "Australia/Sydney", etc. (Use `TimeZoneInfo.TryConvertIanaIdToWindowsId` + case-insensitive lookup)
3. Fix `JsTemporalZonedDateTime.Equals` to compare via primary identifiers, not raw TimeZoneId strings
4. Fix `IntlUtilities.CanonicalizeTimeZoneId` to do proper IANA canonicalization

**Estimated fix: ~100 lines + alias data. Impact: ~25 tests fixed.**

### Fix 4: DST-aware day length in Duration.total/round (~20 tests)
When unit is "days" and `zonedDateTimeRelativeTo` is provided, compute actual day length:
```
startNs = zonedDateTimeRelativeTo.Instant.EpochNanoseconds
dayEndNs = AddDaysToZonedDateTime(zonedDateTimeRelativeTo, 1).Instant.EpochNanoseconds
actualDayLengthNs = dayEndNs - startNs
```
Use `actualDayLengthNs` instead of fixed `86_400_000_000_000`.

**Estimated fix: ~30 lines. Impact: ~20 tests fixed.**

### Fix 5: Intl.Locale validation fixes (~29 tests)
Multiple small fixes:
1. `TryNormalizeCalendar`: Accept any valid Unicode type subtag, not just known calendars
2. `caseFirst` validation: Use case-sensitive comparison
3. Constructor without `new`: Throw TypeError, not RangeError
4. Getter order: Move `variants` before `calendar`, move `numberingSystem` after `numeric`
5. Minimize/maximize: Fix variant subtag duplication

**Estimated fix: ~50 lines across files. Impact: ~20 tests fixed.**

### Fix 6: getTimeZoneTransition precision (~12 tests)
Use nanosecond-precise transition detection. Consider using ICU4N or a bundled IANA tzdb parser for accurate transition times instead of relying on .NET's `GetAdjustmentRules()`.

**Estimated fix: Complex. Impact: ~12 tests fixed.**

## Test Plan
- [ ] Fix 1: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~Temporal_PlainYearMonth_from&FullyQualifiedName~intl402"`
- [ ] Fix 2: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~toLocaleString&FullyQualifiedName~intl402"`
- [ ] Fix 3: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~canonicalize&FullyQualifiedName~intl402"`
- [ ] Fix 4: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~Temporal_Duration_prototype_total&FullyQualifiedName~intl402"`
- [ ] Fix 5: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~Intl402Tests.Locale"`
- [ ] Run full regression: `dotnet test tests/Asynkron.JsEngine.Tests`

## Key File Locations
| File | Lines | Issue |
|------|-------|-------|
| `TemporalHelper.cs` | 9048-9074 | ResolveTemporalCalendarId only accepts "iso8601" |
| `TemporalHelper.cs` | 223-245 | ValidateCalendarId (correct, but unused by above) |
| `TemporalHelper.cs` | 767,1514,1966,2312,2829,3468,3781 | Stub toLocaleString methods |
| `TemporalHelper.cs` | 7407-7416 | Fixed 24h day in Duration.total |
| `TemporalHelper.cs` | 10357-10381 | Minimal CanonicalizeTimeZoneId |
| `TemporalHelper.cs` | 3099-3247 | getTimeZoneTransition implementation |
| `JsTemporalZonedDateTime.cs` | 821 | String equality for timezone comparison |
| `IntlDateTimeFormatPrototype.cs` | 1651-1677 | ToEpochMilliseconds lacks Temporal support |
| `IntlUtilities.cs` | 237-241 | TryNormalizeCalendar too strict |
| `IntlUtilities.cs` | 512-520 | CanonicalizeTimeZoneId too basic |
| `IntlLocaleConstructor.cs` | 114-122 | Calendar option validation too strict |

## Additional Notes
- Fix 1 (calendar recognition) is by far the easiest and highest-impact fix: ~5 lines for ~40 tests
- Fixes 2 and 3 together would resolve ~85 tests but require more work
- The timezone alias issue (Fix 3) affects both Temporal and Intl modules — a shared alias table would benefit both
- The DST fix (Fix 4) is conceptually simple but needs careful testing with edge cases around DST boundaries
- Some `getTimeZoneTransition` tests may require bundling IANA tzdb data rather than relying on .NET system timezone info, which may have different precision or different transition data depending on the OS version

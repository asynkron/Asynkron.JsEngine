# Remaining Test262 Gaps — 2026-03-19

> [!NOTE]
> The totals and category breakdowns in this document are a historical snapshot from 2026-03-19.
> Do not use this file alone to pick current implementation work.
> For current regression-session evidence, use:
> - `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt` (master list)
> - `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/` (subsystem packs)
> - `./tools/run-test262-regressions.sh --list` (list available packs)

## Current Workflow (Source of Truth)

1. Start from `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt` for the active owning failure list.
2. Choose one focused slice from `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/`.
3. Run `./tools/run-test262-regressions.sh --list` to confirm available pack names before selecting a pack.
4. Use the sections below only as historical context for why specific clusters were grouped.

**Baseline: 89,127 passed / 92,975 total (95.9%)**
**38 todo files, ~1,200 remaining test entries**

---

## Historical Snapshot Breakdown (2026-03-19)

## 1. RegExp — .NET Regex Engine Limitations (~930 tests)

### 1.1 Unicode Property Escapes with Lone Surrogates (636 tests)
**File:** `RegExp_propertyEscapes_generated.md`
**Problem:** .NET's `System.Text.RegularExpressions` cannot match lone surrogates (0xD800-0xDFFF) with Unicode property escapes like `\p{Any}`, `\P{ASCII_Hex_Digit}`. When test strings contain lone surrogates (valid UTF-16 but not valid Unicode scalar values), .NET regex fails to match them.
**Fix needed:** Custom surrogate-aware matching layer that pre-processes patterns with `\p{...}` / `\P{...}` to handle lone surrogates explicitly, or a custom regex engine for Unicode property escapes.

### 1.2 Unicode Set Notation — `v` flag (152 tests)
**File:** `RegExp_unicodeSets_generated.md`
**Problem:** The `v` flag (unicodeSets) enables set operations in character classes: `[A--B]` (subtraction), `[A&&B]` (intersection), `[[a-z]&&\p{ASCII}]` (nested). .NET regex has no equivalent syntax.
**Fix needed:** Custom character class parser that compiles `v`-flag patterns into equivalent .NET character classes by resolving set operations at compile time.

### 1.3 RegExp Modifiers — `(?ims:...)` Inline Syntax (34 tests)
**File:** `RegExp_regexpModifiers.md`
**Problem:** ECMAScript 2025 added inline modifier syntax `(?ims-ims:...)` to enable/disable flags within a pattern. .NET supports `(?ims)` but not the ECMAScript syntax for toggling flags on subexpressions.
**Fix needed:** Pattern rewriter that translates ECMAScript modifier groups into equivalent .NET constructs.

### 1.4 Duplicate Named Capture Groups (14+4 tests)
**Files:** `RegExp_namedGroups.md`, `String_prototype_match.md`
**Problem:** ES2025 allows duplicate named groups across alternations: `/(?<x>a)|(?<x>b)/`. .NET throws `ArgumentException` for duplicate group names.
**Fix needed:** Group name deduplication in pattern normalization (append branch index), then map results back to original names.

### 1.5 LookBehind Backreferences (4 tests)
**File:** `RegExp_lookBehind.md`
**Problem:** ECMAScript lookbehind evaluates right-to-left with specific capture group reset semantics that differ from .NET's implementation.
**Fix needed:** Custom lookbehind evaluation or pattern transformation.

### 1.6 Misc RegExp (90+6+2 tests)
**Files:** `RegExp.md`, `RegExp_prototype_exec.md`, `RegExp_prototype_Symbol_split.md`
**Problem:** Mixed issues — capture group reset in quantified groups (`.NET keeps last match, JS resets`), AnnexB `Symbol.match-getter-recompiles-source`, and non-participating group semantics.

---

## 2. ShadowRealm — Incomplete Implementation (84 tests)

### 2.1 ShadowRealm.prototype.evaluate (74 tests)
**File:** `ShadowRealm_prototype_evaluate.md`
**Problem:** Error wrapping across realm boundaries incomplete — errors thrown in shadow realm must be wrapped into TypeError in the calling realm. Also: `globalThis` available properties, `import()` support, `eval` and `Function` constructor behavior in shadow realm.
**Fix needed:** Complete error wrapping (HostCallJobCallback), proper `globalThis` setup with all required builtins, `import()` support.

### 2.2 WrappedFunction (10 tests)
**File:** `ShadowRealm_WrappedFunction.md`
**Problem:** Wrapped functions need correct `name` and `length` properties (retrieved from target, TypeError on throw), and proper `call`/`apply` semantics across realm boundaries.
**Fix needed:** Implement WrappedFunction exotic object per spec with name/length getter error handling.

---

## 3. Annex B — Function-in-Block in Eval/Global (99 tests)

### 3.1 Direct Eval Function Declarations (49 tests)
**File:** `Language_evalCode_direct.md`

### 3.2 Indirect Eval Function Declarations (30 tests)
**File:** `Language_evalCode_indirect.md`

### 3.3 Global Code Function Declarations (20 tests)
**File:** `Language_globalCode.md`

**Problem (all 3):** Annex B.3.2/B.3.3 function-in-block semantics in eval and global code. When `if/else` blocks contain function declarations, these should be hoisted to the enclosing function/global scope with specific update/init/block-scoping semantics. The engine handles function-scope Annex B hoisting but not global/eval scope.
**Fix needed:** Implement Annex B function hoisting for global and eval scopes, including `HasRestrictedGlobalProperty` check, existing fn update vs new global init, and block-scoping interactions.

---

## 4. TypedArray — Cross-Realm & Resizable Buffers (34 tests)

### 4.1 Cross-Realm Prototype Resolution (16 tests)
**Files:** All `TypedArrayConstructors_ctors_*` files with `proto-from-ctor-realm` tests
**Problem:** When constructing a TypedArray with `Reflect.construct(Int8Array, args, OtherRealmInt8Array)`, the prototype should come from the other realm's constructor. The engine doesn't resolve cross-realm intrinsic prototypes.
**Fix needed:** `GetPrototypeFromConstructor` that walks the `newTarget`'s realm to find the correct intrinsic prototype.

### 4.2 Resizable ArrayBuffer (10 tests)
**Files:** `TypedArray_prototype_set.md`, `TypedArray_prototype_subarray.md`
**Problem:** Tests involve `ArrayBuffer` with `{maxByteLength}` option (resizable). The engine has partial resizable buffer support but `grow`/`shrink` during getter callbacks isn't fully handled.
**Fix needed:** Complete resizable ArrayBuffer implementation with proper bounds rechecking after each observable operation.

### 4.3 SharedArrayBuffer (4 tests)
**Files:** `TypedArrayConstructors_ctors_bufferArg.md`, `ctorsBigint_bufferArg.md`
**Problem:** `proto-from-ctor-realm-sab` tests require SharedArrayBuffer support.
**Fix needed:** SharedArrayBuffer implementation.

### 4.4 Prototype Chain [[Set]] (8 tests)
**Files:** `TypedArrayConstructors_internals_Set.md`, `Set_BigInt.md`
**Problem:** When a TypedArray is in the prototype chain of an ordinary object, `[[Set]]` on the ordinary object should check the TypedArray's integer-indexed property semantics.
**Fix needed:** Propagate TypedArray exotic [[Set]] through prototype chain lookups.

---

## 5. Temporal — Intl402 & Edge Cases (~25 tests)

### 5.1 Non-ISO Calendar Support (intl402)
**Files:** All 11 Temporal todo files
**Problem:** Tests require non-ISO calendars (gregory, hebrew, chinese, japanese) for calendar-aware operations. The engine only supports `iso8601` calendar.
**Fix needed:** Implement `CalendarDateAdd`, `CalendarDateUntil`, `CalendarFields` for non-ISO calendars via `Intl.DateTimeFormat` integration.

### 5.2 Extreme Date Range (2-4 tests)
**Problem:** `DateTimeOffset` overflow for years outside ~-271,821 to +275,760. Some Temporal tests use years like ±999,999.
**Fix needed:** BigInteger-based date computation for extreme ranges, bypassing `DateTimeOffset`.

### 5.3 DST Edge Cases (2-4 tests)
**Problem:** Sub-minute UTC offset historical timezone transitions (e.g., Africa/Monrovia pre-1972). macOS timezone data may not include these.
**Fix needed:** Custom timezone offset lookup or IANA tzdata integration.

---

## 6. String — Locale-Specific Casing (6 tests)

**File:** `String_prototype_toLocaleLowerCase.md`
**Problem:** Turkish/Azerbaijani dotted-I rules (`İ` → `i`, `I` → `ı`) and Lithuanian accent-retention rules require locale-specific Unicode case mapping that goes beyond `CultureInfo.TextInfo.ToLower()`.
**Fix needed:** Implement special casing rules from Unicode SpecialCasing.txt for Turkish, Azerbaijani, and Lithuanian locales.

---

## Summary by Fixability

| Category | Tests | Effort | Approach |
|----------|-------|--------|----------|
| RegExp property escapes (surrogates) | 636 | High | Custom surrogate matching layer |
| RegExp unicodeSets (v flag) | 152 | High | Custom set operation compiler |
| Annex B eval/global hoisting | 99 | Medium | Scope analysis extension |
| RegExp misc (.NET limits) | 98 | Medium | Pattern transformation |
| ShadowRealm | 84 | Medium | Error wrapping, globalThis setup |
| RegExp modifiers | 34 | Medium | Pattern rewriter |
| TypedArray cross-realm | 16 | Medium | GetPrototypeFromConstructor |
| Temporal intl402 calendars | ~25 | High | Non-ISO calendar system |
| TypedArray resizable/SAB | 14 | Medium | Buffer resize callbacks |
| TypedArray [[Set]] chain | 8 | Low | Prototype chain fix |
| String locale casing | 6 | Low | SpecialCasing.txt lookup |
| RegExp duplicate groups | 18 | Low | Group name deduplication |
| RegExp lookBehind | 4 | High | Custom evaluator |
| Temporal extreme range | 4 | Low | BigInteger dates |

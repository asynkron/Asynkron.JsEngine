# ADR 0156: Keep Intl numbering-system locale resolution shared

## Status

Accepted

## Context

Issue `autrun-discmtty0hgw-585866a96b` / PR #2007 was a recurring
code-reduction slice over the Intl constructor family. `Intl.NumberFormat`,
`Intl.RelativeTimeFormat`, and `Intl.DurationFormat` each carried a private
copy of the same numbering-system and resolved-locale selection algorithm.

The duplicated helper parsed Unicode extension keywords, extracted `nu`,
removed Unicode extensions for the base locale, applied the option-over-
extension-over-default precedence, preserved the resolved locale when the
extension still described the selected numbering system, and fell back to
`latn` plus the base locale when no supported numbering system was present.

The implementation moved that exact resolver to
`IntlUtilities.ResolveNumberingSystemAndLocale(...)` and updated all three
constructors to use it. Focused Intl constructor tests for NumberFormat,
RelativeTimeFormat, and DurationFormat remained green.

## Decision

The shared `IntlUtilities.ResolveNumberingSystemAndLocale(...)` helper owns the
common ECMA-402 numbering-system locale-resolution behavior for Intl
constructors that only need `numberingSystem` plus locale output.

Future constructor work should:

1. read constructor-specific options in the constructor's required spec order;
2. validate and normalize the `numberingSystem` option before resolution;
3. call the shared resolver instead of adding a constructor-local clone; and
4. prove changes with the affected constructor group plus at least one sibling
   constructor when the shared helper changes.

Constructors with additional extension interaction, such as DateTimeFormat's
calendar and hour-cycle handling, may keep a specialized resolver until a
follow-up proves the broader merge is semantics-preserving.

## Consequences

- Numbering-system precedence and resolved-locale extension retention now have
  one implementation for the shared constructor family.
- Future Intl code-reduction runs should search for resolver-shaped private
  helpers before adding another local copy.
- Shared helper changes must be treated as cross-constructor behavior changes,
  not as a single-constructor cleanup.

## Related

- `.claude/rules/ecmascript-intl-language-tags.md`
- `src/Asynkron.JsEngine/StdLib/Intl/IntlUtilities.cs`
- `src/Asynkron.JsEngine/StdLib/Intl/IntlNumberFormatConstructor.cs`
- `src/Asynkron.JsEngine/StdLib/Intl/IntlRelativeTimeFormatConstructor.cs`
- `src/Asynkron.JsEngine/StdLib/Intl/IntlDurationFormatConstructor.cs`

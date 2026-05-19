# ECMAScript Abstract Operation Order

When implementing ECMAScript built-ins, model the named abstract operation
sequence directly before adding local type guards or host-runtime shortcuts.

## Rules

1. Preserve observable coercion order. If the spec first performs an operation
   such as `ToDateTimeFormattable`, `ToNumber`, `ToPropertyKey`, or `Get`, do
   that before validating later same-kind, same-type, or option compatibility
   constraints.
2. Store the result of the abstract operation when later checks depend on its
   normalized shape. Re-detecting from the original `JsValue` can move errors or
   side effects ahead of required coercions.
3. For Intl Temporal formatting, route supported Temporal values through their
   effective Temporal slots instead of falling back to epoch milliseconds or
   `valueOf` behavior. The slots define which date/time fields are meaningful
   for `PlainDate`, `PlainDateTime`, `PlainTime`, `PlainYearMonth`, and
   `PlainMonthDay`.
4. Keep unsupported Temporal kinds explicit. `Temporal.ZonedDateTime` and
   distinct Temporal operand kinds should fail at the spec point that follows
   conversion to formattable operands, not through incidental host conversion
   failures.
5. Add focused coverage for both the error-order case and the successful
   normalized path. Include the exact Test262 method group or file cluster when
   the issue came from Test262.

## Why

Issue #767 / PR #941 fixed `Intl.DateTimeFormat.prototype.formatRange` after
Temporal operands were handled too much like epoch-millisecond values. The
initial focused Test262 run exposed two durable traps: operands must be
converted to DateTime-formattable values before Temporal kind validation, and
supported Temporal `Plain*` objects must format through effective Temporal
slots rather than falling through to Date/valueOf behavior. The fix added local
Temporal range coverage and passed the focused `DateTimeFormat_prototype_formatRange`
Test262 method group.

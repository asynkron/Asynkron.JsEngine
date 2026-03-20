# Crashed Tests Analysis — 2026-03-20

**Total: 700 crash entries (456 unique test files)**

Crashes are from the testrunner killing worker processes that exceed the 30s timeout,
OOM, or stack overflow. These are NOT assertion failures — the test process dies.

## Key Finding

Two problems:

1. **Genuinely slow tests (~227 RegExp property-escapes)** — each generates massive
   regex patterns with thousands of Unicode code point ranges and calls
   `String.fromCodePoint.apply(null, [...10K+ code points...])`. They take 10-30s each
   **even solo**. With 30s timeout, many get killed → "crashed". These choke with or
   without parallelism.

2. **Collateral damage (~473)** — co-scheduled tests that die when a heavy test kills
   the worker process. These pass instantly when run individually.

### Why property-escapes choke
The test pattern: generate `/\p{Alphabetic}/u` → engine expands into massive alternation
of Unicode ranges → test calls `String.fromCodePoint.apply(null, codePoints)` with 10K+
args to verify each. Bottlenecks:
- Building huge alternation pattern string (thousands of ranges)
- .NET regex compilation of massive alternation
- `String.fromCodePoint` processing 10K+ args

### Fix Strategy (to clear the 700 crashes)
1. **Optimize `BuildPropertyEscapePattern`** — use character class `[\u0041-\u005A]`
   instead of alternation `(\u0041|\u0042|...\u005A)`. .NET handles ranges natively.
2. **Cache compiled Regex for Unicode properties** — same property used across tests
3. **Optimize `String.fromCodePoint` bulk path** — already pre-allocated StringBuilder,
   could go further with Span-based construction

## Summary by Area

| Area | Count | Notes |
|------|-------|-------|
| built-ins/RegExp | 242 | |
| language/expressions | 112 | |
| language/statements | 69 | |
| built-ins/Object | 38 | |
| built-ins/Temporal | 36 | |
| built-ins/Array | 27 | |
| built-ins/String | 16 | |
| built-ins/TypedArray | 14 | |
| built-ins/Function | 10 | |
| language/global-code | 8 | |
| intl402/RelativeTimeFormat | 8 | |
| intl402/DateTimeFormat | 8 | |
| built-ins/Proxy | 8 | |
| built-ins/Iterator | 8 | |
| built-ins/decodeURIComponent | 8 | |
| built-ins/decodeURI | 8 | |
| built-ins/Atomics | 8 | |
| built-ins/ArrayBuffer | 8 | |
| built-ins/TypedArrayConstructors | 7 | |
| built-ins/DataView | 7 | |
| language/literals | 6 | |
| built-ins/Date | 6 | |
| language/block-scope | 5 | |
| language/eval-code | 4 | |
| intl402/NumberFormat | 4 | |
| built-ins/Math | 4 | |
| built-ins/Map | 4 | |
| intl402/Temporal | 3 | |
| annexB/language | 3 | |
| annexB/built-ins | 3 | |
| intl402/supportedLocalesOf-unicode-extensions-ignored.js | 2 | |
| built-ins/WeakMap | 2 | |
| built-ins/parseInt | 2 | |
| built-ins/parseFloat | 2 | |

## Category 1: RegExp Property Escapes (227 crashes)

These tests generate massive regex patterns with thousands of Unicode code point ranges.
They OOM or timeout when `String.fromCodePoint.apply()` is called with 10K+ args,
or when the .NET regex engine backtracks on huge alternation patterns.

```
built-ins/RegExp/property-escapes/character-class.js
built-ins/RegExp/property-escapes/generated/Bidi_Control.js
built-ins/RegExp/property-escapes/generated/Case_Ignorable.js
built-ins/RegExp/property-escapes/generated/Cased.js
built-ins/RegExp/property-escapes/generated/Changes_When_Casefolded.js
built-ins/RegExp/property-escapes/generated/Changes_When_Uppercased.js
built-ins/RegExp/property-escapes/generated/Default_Ignorable_Code_Point.js
built-ins/RegExp/property-escapes/generated/Deprecated.js
built-ins/RegExp/property-escapes/generated/Extended_Pictographic.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Close_Punctuation.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Connector_Punctuation.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Currency_Symbol.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Decimal_Number.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Enclosing_Mark.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Format.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Letter_Number.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Letter.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Line_Separator.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Lowercase_Letter.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Mark.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Other_Symbol.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Private_Use.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Punctuation.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Separator.js
built-ins/RegExp/property-escapes/generated/General_Category_-_Space_Separator.js
built-ins/RegExp/property-escapes/generated/ID_Continue.js
built-ins/RegExp/property-escapes/generated/Lowercase.js
built-ins/RegExp/property-escapes/generated/Math.js
built-ins/RegExp/property-escapes/generated/Noncharacter_Code_Point.js
built-ins/RegExp/property-escapes/generated/Pattern_Syntax.js
built-ins/RegExp/property-escapes/generated/Pattern_White_Space.js
built-ins/RegExp/property-escapes/generated/Quotation_Mark.js
built-ins/RegExp/property-escapes/generated/Radical.js
built-ins/RegExp/property-escapes/generated/Regional_Indicator.js
built-ins/RegExp/property-escapes/generated/Script_-_Cham.js
built-ins/RegExp/property-escapes/generated/Script_-_Cherokee.js
built-ins/RegExp/property-escapes/generated/Script_-_Chorasmian.js
built-ins/RegExp/property-escapes/generated/Script_-_Common.js
built-ins/RegExp/property-escapes/generated/Script_-_Coptic.js
built-ins/RegExp/property-escapes/generated/Script_-_Cyrillic.js
built-ins/RegExp/property-escapes/generated/Script_-_Deseret.js
built-ins/RegExp/property-escapes/generated/Script_-_Devanagari.js
built-ins/RegExp/property-escapes/generated/Script_-_Dives_Akuru.js
built-ins/RegExp/property-escapes/generated/Script_-_Dogra.js
built-ins/RegExp/property-escapes/generated/Script_-_Garay.js
built-ins/RegExp/property-escapes/generated/Script_-_Georgian.js
built-ins/RegExp/property-escapes/generated/Script_-_Glagolitic.js
built-ins/RegExp/property-escapes/generated/Script_-_Gothic.js
built-ins/RegExp/property-escapes/generated/Script_-_Grantha.js
built-ins/RegExp/property-escapes/generated/Script_-_Hanifi_Rohingya.js
built-ins/RegExp/property-escapes/generated/Script_-_Hanunoo.js
built-ins/RegExp/property-escapes/generated/Script_-_Hatran.js
built-ins/RegExp/property-escapes/generated/Script_-_Hebrew.js
built-ins/RegExp/property-escapes/generated/Script_-_Hiragana.js
built-ins/RegExp/property-escapes/generated/Script_-_Kawi.js
built-ins/RegExp/property-escapes/generated/Script_-_Kayah_Li.js
built-ins/RegExp/property-escapes/generated/Script_-_Kharoshthi.js
built-ins/RegExp/property-escapes/generated/Script_-_Khitan_Small_Script.js
built-ins/RegExp/property-escapes/generated/Script_-_Khmer.js
built-ins/RegExp/property-escapes/generated/Script_-_Linear_B.js
built-ins/RegExp/property-escapes/generated/Script_-_Lisu.js
built-ins/RegExp/property-escapes/generated/Script_-_Lycian.js
built-ins/RegExp/property-escapes/generated/Script_-_Lydian.js
built-ins/RegExp/property-escapes/generated/Script_-_Mahajani.js
built-ins/RegExp/property-escapes/generated/Script_-_Makasar.js
built-ins/RegExp/property-escapes/generated/Script_-_Mongolian.js
built-ins/RegExp/property-escapes/generated/Script_-_Myanmar.js
built-ins/RegExp/property-escapes/generated/Script_-_Nabataean.js
built-ins/RegExp/property-escapes/generated/Script_-_Nag_Mundari.js
built-ins/RegExp/property-escapes/generated/Script_-_Nandinagari.js
built-ins/RegExp/property-escapes/generated/Script_-_Newa.js
built-ins/RegExp/property-escapes/generated/Script_-_Nko.js
built-ins/RegExp/property-escapes/generated/Script_-_Nushu.js
built-ins/RegExp/property-escapes/generated/Script_-_Nyiakeng_Puachue_Hmong.js
built-ins/RegExp/property-escapes/generated/Script_-_Ogham.js
built-ins/RegExp/property-escapes/generated/Script_-_Pahawh_Hmong.js
built-ins/RegExp/property-escapes/generated/Script_-_Palmyrene.js
built-ins/RegExp/property-escapes/generated/Script_-_Pau_Cin_Hau.js
built-ins/RegExp/property-escapes/generated/Script_-_Phags_Pa.js
built-ins/RegExp/property-escapes/generated/Script_-_Shavian.js
built-ins/RegExp/property-escapes/generated/Script_-_Siddham.js
built-ins/RegExp/property-escapes/generated/Script_-_SignWriting.js
built-ins/RegExp/property-escapes/generated/Script_-_Sinhala.js
built-ins/RegExp/property-escapes/generated/Script_-_Sogdian.js
built-ins/RegExp/property-escapes/generated/Script_-_Sora_Sompeng.js
built-ins/RegExp/property-escapes/generated/Script_-_Telugu.js
built-ins/RegExp/property-escapes/generated/Script_-_Tirhuta.js
built-ins/RegExp/property-escapes/generated/Script_-_Toto.js
built-ins/RegExp/property-escapes/generated/Script_-_Tulu_Tigalari.js
built-ins/RegExp/property-escapes/generated/Script_-_Ugaritic.js
built-ins/RegExp/property-escapes/generated/Script_-_Vai.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Anatolian_Hieroglyphs.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Arabic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Armenian.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Avestan.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Cherokee.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Chorasmian.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Coptic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Cuneiform.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Cypriot.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Cypro_Minoan.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Cyrillic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Devanagari.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Dives_Akuru.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Dogra.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Elbasan.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Elymaic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Ethiopic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Garay.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Gurung_Khema.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Han.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Hangul.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Hanifi_Rohingya.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Khmer.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Khojki.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Khudawadi.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Kirat_Rai.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Lao.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Latin.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Malayalam.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Mandaic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Manichaean.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Masaram_Gondi.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Medefaidrin.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Meetei_Mayek.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Mende_Kikakui.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Meroitic_Cursive.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Meroitic_Hieroglyphs.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Nabataean.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Nandinagari.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_New_Tai_Lue.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Newa.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Nko.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Nyiakeng_Puachue_Hmong.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Ol_Chiki.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Old_Hungarian.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Old_North_Arabian.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Syloti_Nagri.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Tagbanwa.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Tai_Viet.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Tangsa.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Tangut.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Thai.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Tibetan.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Ugaritic.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Vai.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Yezidi.js
built-ins/RegExp/property-escapes/generated/Script_Extensions_-_Zanabazar_Square.js
built-ins/RegExp/property-escapes/generated/Unified_Ideograph.js
built-ins/RegExp/property-escapes/generated/Uppercase.js
```

## Category 2: Language Expression/Statement Tests (181 crashes)

Collateral damage — these tests share a worker process with heavy tests.
When a heavy test OOMs or timeouts, the entire worker dies, taking these with it.
Most pass when run individually.

```
language/block-scope/syntax/redeclaration/var-name-redeclaration-attempt-with-async-generator.js
language/block-scope/syntax/redeclaration/var-name-redeclaration-attempt-with-generator.js
language/block-scope/syntax/redeclaration/var-name-redeclaration-attempt-with-let.js
language/block-scope/syntax/redeclaration/var-redeclaration-attempt-after-async-function.js
language/eval-code/direct/async-func-decl-a-following-parameter-is-named-arguments-declare-arguments.js
language/eval-code/direct/async-func-decl-a-preceding-parameter-is-named-arguments-declare-arguments-and-assign.js
language/eval-code/direct/async-func-decl-fn-body-cntns-arguments-func-decl-declare-arguments.js
language/eval-code/direct/async-func-decl-fn-body-cntns-arguments-lex-bind-declare-arguments-and-assign.js
language/expressions/arrow-function/dstr/syntax-error-ident-ref-finally-escaped.js
language/expressions/arrow-function/dstr/syntax-error-ident-ref-for-escaped.js
language/expressions/arrow-function/dstr/syntax-error-ident-ref-function-escaped.js
language/expressions/arrow-function/dstr/syntax-error-ident-ref-if-escaped.js
language/expressions/arrow-function/dstr/syntax-error-ident-ref-implements-escaped.js
language/expressions/arrow-function/dstr/syntax-error-ident-ref-import-escaped.js
language/expressions/async-generator/named-yield-star-getiter-sync-returns-abrupt.js
language/expressions/async-generator/named-yield-star-getiter-sync-returns-null-throw.js
language/expressions/async-generator/named-yield-star-getiter-sync-returns-number-throw.js
language/expressions/async-generator/named-yield-star-getiter-sync-returns-string-throw.js
language/expressions/async-generator/named-yield-star-getiter-sync-returns-symbol-throw.js
language/expressions/bitwise-or/S11.10.3_A3_T2.5.js
language/expressions/bitwise-or/S11.10.3_A3_T2.7.js
language/expressions/bitwise-or/S11.10.3_A3_T2.8.js
language/expressions/bitwise-or/S11.10.3_A3_T2.9.js
language/expressions/class/dstr/async-gen-meth-static-dflt-ary-ptrn-elem-id-iter-val-err.js
language/expressions/class/dstr/async-gen-meth-static-dflt-ary-ptrn-elem-id-iter-val.js
language/expressions/class/dstr/async-gen-meth-static-dflt-ary-ptrn-elem-obj-id-init.js
language/expressions/class/dstr/async-gen-meth-static-dflt-ary-ptrn-elem-obj-id.js
language/expressions/class/dstr/async-private-gen-meth-static-dflt-ary-ptrn-elem-obj-prop-id-init.js
language/expressions/class/dstr/async-private-gen-meth-static-dflt-ary-ptrn-elem-obj-prop-id.js
language/expressions/class/dstr/async-private-gen-meth-static-dflt-ary-ptrn-elision-exhausted.js
language/expressions/class/dstr/async-private-gen-meth-static-dflt-ary-ptrn-elision.js
language/expressions/class/dstr/async-private-gen-meth-static-dflt-ary-ptrn-empty.js
language/expressions/class/dstr/private-gen-meth-static-obj-ptrn-id-init-fn-name-arrow.js
language/expressions/class/dstr/private-gen-meth-static-obj-ptrn-id-init-fn-name-class.js
language/expressions/class/dstr/private-gen-meth-static-obj-ptrn-id-init-fn-name-cover.js
language/expressions/class/dstr/private-gen-meth-static-obj-ptrn-id-init-fn-name-fn.js
language/expressions/class/dstr/private-gen-meth-static-obj-ptrn-id-init-fn-name-gen.js
language/expressions/class/elements/derived-cls-direct-eval-err-contains-supercall.js
language/expressions/class/elements/derived-cls-indirect-eval-contains-superproperty-1.js
language/expressions/class/elements/derived-cls-indirect-eval-contains-superproperty-2.js
language/expressions/class/elements/derived-cls-indirect-eval-err-contains-supercall-1.js
language/expressions/class/elements/derived-cls-indirect-eval-err-contains-supercall-2.js
language/expressions/class/elements/derived-cls-indirect-eval-err-contains-supercall.js
language/expressions/class/elements/same-line-gen-rs-static-async-method-privatename-identifier-alt.js
language/expressions/class/elements/same-line-gen-rs-static-async-method-privatename-identifier.js
language/expressions/class/elements/same-line-gen-rs-static-generator-method-privatename-identifier-alt.js
language/expressions/class/elements/same-line-gen-rs-static-generator-method-privatename-identifier.js
language/expressions/class/elements/static-field-declaration.js
language/expressions/class/elements/static-literal-init-err-contains-super.js
language/expressions/class/elements/static-private-fields-proxy-default-handler-throws.js
language/expressions/class/elements/static-private-getter-access-on-inner-arrow-function.js
language/expressions/class/elements/static-private-getter-access-on-inner-class.js
language/expressions/compound-assignment/S11.13.2_A4.1_T2.5.js
language/expressions/compound-assignment/S11.13.2_A4.1_T2.6.js
language/expressions/compound-assignment/S11.13.2_A4.1_T2.7.js
language/expressions/compound-assignment/S11.13.2_A4.1_T2.8.js
language/expressions/compound-assignment/S11.13.2_A4.1_T2.9.js
language/expressions/compound-assignment/S11.13.2_A4.2_T1.2.js
language/expressions/equals/coerce-symbol-to-prim-return-obj.js
language/expressions/equals/coerce-symbol-to-prim-return-prim.js
language/expressions/equals/get-symbol-to-prim-err.js
language/expressions/equals/S11.9.1_A1.js
language/expressions/equals/S11.9.1_A2.1_T1.js
language/expressions/greater-than/S11.8.2_A2.1_T1.js
language/expressions/greater-than/S11.8.2_A2.1_T2.js
language/expressions/greater-than/S11.8.2_A2.1_T3.js
language/expressions/greater-than/S11.8.2_A2.2_T1.js
language/expressions/new/S11.2.2_A4_T3.js
language/expressions/new/S11.2.2_A4_T4.js
language/expressions/new/S11.2.2_A4_T5.js
language/expressions/new/spread-err-mult-err-expr-throws.js
language/expressions/new/spread-err-mult-err-iter-get-value.js
language/expressions/object/dstr/gen-meth-dflt-ary-ptrn-rest-ary-rest.js
language/expressions/object/dstr/gen-meth-dflt-ary-ptrn-rest-id-direct.js
language/expressions/object/dstr/gen-meth-dflt-ary-ptrn-rest-id-elision-next-err.js
language/expressions/strict-does-not-equals/S11.9.5_A2.1_T3.js
language/expressions/strict-does-not-equals/S11.9.5_A2.4_T1.js
language/expressions/strict-does-not-equals/S11.9.5_A2.4_T2.js
language/expressions/strict-does-not-equals/S11.9.5_A2.4_T3.js
language/expressions/strict-does-not-equals/S11.9.5_A2.4_T4.js
language/global-code/script-decl-func-dups.js
language/global-code/script-decl-func-err-non-configurable.js
language/global-code/script-decl-func-err-non-extensible.js
language/global-code/script-decl-func.js
language/global-code/script-decl-lex-deletion.js
language/literals/numeric/S7.8.3_A4.1_T6.js
language/literals/numeric/S7.8.3_A4.2_T2.js
language/literals/numeric/S7.8.3_A4.2_T3.js
language/literals/numeric/S7.8.3_A4.2_T4.js
language/literals/numeric/S7.8.3_A4.2_T5.js
language/statements/async-generator/dstr/dflt-ary-ptrn-elem-ary-elision-iter.js
language/statements/async-generator/dstr/dflt-ary-ptrn-elem-ary-empty-init.js
language/statements/async-generator/dstr/dflt-ary-ptrn-elem-ary-empty-iter.js
language/statements/async-generator/dstr/dflt-ary-ptrn-elem-ary-rest-init.js
language/statements/async-generator/dstr/dflt-ary-ptrn-elem-ary-rest-iter.js
language/statements/class/async-method-static/forbidden-ext/b2/cls-decl-async-meth-static-forbidden-ext-indirect-access-own-prop-caller-get.js
language/statements/class/decorator/syntax/class-valid/decorator-member-expr-private-identifier.js
language/statements/class/decorator/syntax/valid/class-element-decorator-call-expr-identifier-reference.js
language/statements/class/decorator/syntax/valid/class-element-decorator-member-expr-decorator-member-expr.js
language/statements/class/decorator/syntax/valid/class-element-decorator-member-expr-identifier-reference.js
language/statements/class/decorator/syntax/valid/class-element-decorator-parenthesized-expr-identifier-reference.js
language/statements/class/dstr/async-private-gen-meth-static-ary-ptrn-elem-id-init-fn-name-cover.js
language/statements/class/dstr/async-private-gen-meth-static-ary-ptrn-elem-id-init-fn-name-gen.js
language/statements/class/dstr/async-private-gen-meth-static-ary-ptrn-elem-id-init-hole.js
language/statements/class/dstr/async-private-gen-meth-static-ary-ptrn-elem-id-init-skipped.js
language/statements/class/dstr/async-private-gen-meth-static-ary-ptrn-elem-id-init-undef.js
language/statements/class/dstr/async-private-gen-meth-static-ary-ptrn-elem-id-iter-complete.js
language/statements/class/dstr/meth-static-dflt-ary-init-iter-get-err.js
language/statements/class/dstr/meth-static-dflt-ary-init-iter-no-close.js
language/statements/class/dstr/meth-static-dflt-ary-name-iter-val.js
language/statements/class/dstr/meth-static-dflt-ary-ptrn-elem-ary-elem-init.js
language/statements/class/dstr/meth-static-dflt-ary-ptrn-elem-ary-elem-iter.js
language/statements/class/elements/same-line-gen-rs-private-setter.js
language/statements/class/elements/same-line-gen-rs-privatename-identifier-alt.js
language/statements/class/elements/same-line-gen-rs-privatename-identifier-initializer-alt.js
language/statements/class/elements/same-line-gen-rs-privatename-identifier-initializer.js
language/statements/class/elements/same-line-gen-rs-privatename-identifier.js
language/statements/for-of/dstr/obj-id-init-fn-name-arrow.js
language/statements/for-of/dstr/obj-id-init-fn-name-class.js
language/statements/for-of/dstr/obj-id-init-fn-name-cover.js
language/statements/for-of/dstr/obj-id-init-fn-name-fn.js
language/statements/for-of/dstr/obj-id-init-fn-name-gen.js
language/statements/for-of/dstr/obj-id-init-in.js
language/statements/for-of/labelled-fn-stmt-lhs.js
language/statements/for-of/labelled-fn-stmt-var.js
language/statements/for-of/let-array-with-newline.js
language/statements/for-of/let-block-with-newline.js
language/statements/for-of/let-identifier-with-newline.js
language/statements/function/dstr/ary-ptrn-elem-ary-elem-iter.js
language/statements/function/dstr/ary-ptrn-elem-ary-elision-init.js
language/statements/function/dstr/ary-ptrn-elem-ary-elision-iter.js
language/statements/function/dstr/ary-ptrn-elem-ary-empty-init.js
language/statements/function/dstr/ary-ptrn-elem-ary-empty-iter.js
language/statements/let/dstr/ary-ptrn-rest-obj-prop-id.js
language/statements/let/dstr/obj-init-null.js
language/statements/let/dstr/obj-init-undefined.js
language/statements/let/dstr/obj-ptrn-empty.js
language/statements/let/dstr/obj-ptrn-id-get-value-err.js
language/statements/let/dstr/obj-ptrn-id-init-fn-name-arrow.js
```

## Category 3: Built-in Object/Array/String/Function etc. (remaining)

Mix of collateral and genuine timeout/OOM issues.

```
annexB/built-ins/String/prototype/bold/prop-desc.js
annexB/built-ins/String/prototype/substr/start-and-length-as-numbers.js
annexB/language/function-code/if-decl-else-stmt-func-init.js
annexB/language/function-code/if-decl-else-stmt-func-skip-dft-param.js
annexB/language/function-code/if-decl-else-stmt-func-skip-early-err-for.js
built-ins/Array/prototype/concat/Array.prototype.concat_large-typed-array.js
built-ins/Array/prototype/concat/S15.4.4.4_A2_T2.js
built-ins/Array/prototype/concat/S15.4.4.4_A3_T3.js
built-ins/Array/prototype/filter/15.4.4.20-6-4.js
built-ins/Array/prototype/filter/15.4.4.20-6-5.js
built-ins/Array/prototype/forEach/15.4.4.18-7-c-ii-1.js
built-ins/Array/prototype/indexOf/15.4.4.14-10-1.js
built-ins/Array/prototype/lastIndexOf/15.4.4.15-9-1.js
built-ins/Array/prototype/map/15.4.4.19-8-c-i-31.js
built-ins/Array/prototype/map/15.4.4.19-8-c-i-4.js
built-ins/Array/prototype/map/15.4.4.19-8-c-i-5.js
built-ins/Array/prototype/map/15.4.4.19-8-c-i-6.js
built-ins/Array/prototype/some/15.4.4.17-7-c-ii-2.js
built-ins/Array/prototype/sort/resizable-buffer-default-comparator.js
built-ins/Array/prototype/sort/S15.4.4.11_A2.1_T2.js
built-ins/Array/prototype/sort/S15.4.4.11_A2.1_T3.js
built-ins/Array/prototype/sort/S15.4.4.11_A2.2_T1.js
built-ins/ArrayBuffer/prototype/slice/length.js
built-ins/ArrayBuffer/prototype/slice/negative-start.js
built-ins/ArrayBuffer/prototype/slice/nonconstructor.js
built-ins/ArrayBuffer/prototype/slice/not-a-constructor.js
built-ins/ArrayBuffer/prototype/slice/number-conversion.js
built-ins/Atomics/waitAsync/good-views.js
built-ins/Atomics/waitAsync/nan-for-timeout-agent.js
built-ins/Atomics/waitAsync/no-spurious-wakeup-no-operation.js
built-ins/Atomics/waitAsync/no-spurious-wakeup-on-add.js
built-ins/DataView/prototype/setBigInt64/detached-buffer.js
built-ins/DataView/prototype/setBigInt64/index-check-before-value-conversion.js
built-ins/DataView/prototype/setBigInt64/index-is-out-of-range.js
built-ins/DataView/prototype/setBigInt64/length.js
built-ins/Date/prototype/getUTCDate/this-value-non-date.js
built-ins/Date/prototype/getUTCDay/length.js
built-ins/Date/prototype/getUTCDay/name.js
built-ins/Date/prototype/getUTCDay/not-a-constructor.js
built-ins/decodeURI/S15.1.3.1_A1.12_T2.js
built-ins/decodeURI/S15.1.3.1_A1.12_T3.js
built-ins/decodeURI/S15.1.3.1_A1.13_T1.js
built-ins/decodeURI/S15.1.3.1_A1.13_T2.js
built-ins/decodeURIComponent/S15.1.3.2_A1.12_T1.js
built-ins/decodeURIComponent/S15.1.3.2_A1.12_T2.js
built-ins/decodeURIComponent/S15.1.3.2_A1.12_T3.js
built-ins/decodeURIComponent/S15.1.3.2_A1.14_T2.js
built-ins/decodeURIComponent/S15.1.3.2_A1.14_T3.js
built-ins/Function/property-order.js
built-ins/Function/proto-from-ctor-realm-prototype.js
built-ins/Function/proto-from-ctor-realm.js
built-ins/Function/prototype/toString/built-in-function-object.js
built-ins/Function/S10.1.1_A1_T3.js
built-ins/Iterator/prototype/take/length.js
built-ins/Iterator/prototype/take/limit-greater-than-or-equal-to-total.js
built-ins/Iterator/prototype/take/limit-less-than-total.js
built-ins/Iterator/prototype/take/limit-rangeerror.js
built-ins/Iterator/prototype/take/limit-tonumber-throws.js
built-ins/Map/iterator-item-first-entry-returns-abrupt.js
built-ins/Map/iterator-item-second-entry-returns-abrupt.js
built-ins/Math/max/not-a-constructor.js
built-ins/Math/max/prop-desc.js
built-ins/Object/create/15.2.3.5-4-76.js
built-ins/Object/create/15.2.3.5-4-77.js
built-ins/Object/create/15.2.3.5-4-78.js
built-ins/Object/create/15.2.3.5-4-79.js
built-ins/Object/defineProperty/15.2.3.6-3-148.js
built-ins/Object/defineProperty/15.2.3.6-3-149-1.js
built-ins/Object/defineProperty/15.2.3.6-3-149.js
built-ins/Object/defineProperty/15.2.3.6-3-15.js
built-ins/Object/defineProperty/15.2.3.6-3-151.js
built-ins/Object/defineProperty/15.2.3.6-4-289.js
built-ins/Object/defineProperty/15.2.3.6-4-290.js
built-ins/Object/defineProperty/15.2.3.6-4-291-1.js
built-ins/Object/defineProperty/15.2.3.6-4-291.js
built-ins/Object/defineProperty/15.2.3.6-4-292-1.js
built-ins/Object/defineProperty/15.2.3.6-4-292-2.js
built-ins/Object/defineProperty/15.2.3.6-4-292.js
built-ins/Object/fromEntries/string-entry-string-object-succeeds.js
built-ins/Object/fromEntries/supports-symbols.js
built-ins/Object/fromEntries/uses-define-semantics.js
built-ins/Object/fromEntries/uses-keys-not-iterator.js
built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-0-1.js
built-ins/Object/prototype/__proto__/set-ordinary-obj.js
built-ins/Object/prototype/constructor/S15.2.4.1_A1_T1.js
built-ins/Object/prototype/constructor/S15.2.4.1_A1_T2.js
built-ins/Object/prototype/hasOwnProperty/8.12.1-1_10.js
built-ins/Object/prototype/hasOwnProperty/8.12.1-1_11.js
built-ins/parseFloat/S15.1.2.3_A6.js
built-ins/parseInt/S15.1.2.2_A8.js
built-ins/Proxy/apply/trap-is-undefined-target-is-proxy.js
built-ins/Proxy/construct/arguments-realm.js
built-ins/Proxy/construct/call-parameters-new-target.js
built-ins/Proxy/construct/call-parameters.js
built-ins/RegExp/character-class-escape-non-whitespace.js
built-ins/RegExp/CharacterClassEscapes/character-class-digit-class-escape-negative-cases.js
built-ins/RegExp/CharacterClassEscapes/character-class-non-digit-class-escape-positive-cases.js
built-ins/RegExp/CharacterClassEscapes/character-class-non-whitespace-class-escape-positive-cases.js
built-ins/RegExp/CharacterClassEscapes/character-class-non-word-class-escape-positive-cases.js
built-ins/RegExp/S15.10.2.15_A1_T40.js
built-ins/RegExp/S15.10.2.15_A1_T41.js
built-ins/RegExp/S15.10.2.15_A1_T5.js
built-ins/String/prototype/trim/15.5.4.20-3-7.js
built-ins/String/prototype/trim/15.5.4.20-3-8.js
built-ins/String/prototype/trim/15.5.4.20-3-9.js
built-ins/String/prototype/trim/15.5.4.20-4-1.js
built-ins/String/prototype/trim/15.5.4.20-4-10.js
built-ins/String/S15.5.1.1_A1_T19.js
built-ins/String/S15.5.1.1_A1_T2.js
built-ins/String/S15.5.1.1_A1_T3.js
built-ins/String/S15.5.1.1_A1_T4.js
built-ins/String/S15.5.1.1_A1_T5.js
built-ins/Temporal/Duration/prototype/total/relativeto-undefined-throw-on-calendar-units.js
built-ins/Temporal/Duration/prototype/total/relativeto-zoneddatetime-with-fractional-days.js
built-ins/Temporal/Duration/prototype/total/rounds-calendar-units-in-durations-without-calendar-units.js
built-ins/Temporal/Duration/prototype/total/rounds-durations-with-calendar-units.js
built-ins/Temporal/Instant/prototype/until/roundingmode-expand.js
built-ins/Temporal/Instant/prototype/until/roundingmode-floor.js
built-ins/Temporal/Instant/prototype/until/roundingmode-halfCeil.js
built-ins/Temporal/PlainDateTime/compare/argument-string-multiple-time-zone.js
built-ins/Temporal/PlainDateTime/compare/argument-string-time-separators.js
built-ins/Temporal/PlainDateTime/compare/argument-string-time-zone-annotation.js
built-ins/Temporal/PlainDateTime/compare/argument-string-unknown-annotation.js
built-ins/Temporal/PlainDateTime/compare/argument-string-with-utc-designator.js
built-ins/Temporal/PlainDateTime/compare/argument-wrong-type.js
built-ins/Temporal/PlainDateTime/prototype/toString/calendarname-undefined.js
built-ins/Temporal/PlainDateTime/prototype/toString/calendarname-wrong-type.js
built-ins/Temporal/PlainDateTime/prototype/toString/fractionalseconddigits-auto.js
built-ins/Temporal/PlainDateTime/prototype/toString/fractionalseconddigits-nan.js
built-ins/Temporal/PlainTime/prototype/until/argument-cast.js
built-ins/Temporal/PlainTime/prototype/until/argument-string-calendar-annotation.js
built-ins/Temporal/PlainTime/prototype/until/argument-string-critical-unknown-annotation.js
built-ins/Temporal/PlainTime/prototype/until/argument-string-date-with-utc-offset.js
built-ins/Temporal/ZonedDateTime/prototype/round/subclassing-ignored.js
built-ins/Temporal/ZonedDateTime/prototype/round/throws-on-invalid-increments.js
built-ins/Temporal/ZonedDateTime/prototype/round/valid-increments.js
built-ins/Temporal/ZonedDateTime/prototype/second/balance-negative-time-units.js
built-ins/TypedArray/prototype/indexOf/BigInt/detached-buffer-during-fromIndex-returns-minus-one-for-undefined.js
built-ins/TypedArray/prototype/indexOf/BigInt/detached-buffer-during-fromIndex-returns-minus-one-for-zero.js
built-ins/TypedArray/prototype/indexOf/resizable-buffer.js
built-ins/TypedArray/prototype/indexOf/tointeger-fromindex.js
built-ins/TypedArray/prototype/set/this-backed-by-resizable-buffer.js
built-ins/TypedArray/prototype/set/typedarray-arg-set-values-diff-buffer-other-type-conversions-sab.js
built-ins/TypedArray/prototype/set/typedarray-arg-src-backed-by-resizable-buffer.js
built-ins/TypedArray/prototype/set/typedarray-arg-src-range-greather-than-target-throws-rangeerror.js
built-ins/TypedArrayConstructors/Uint32Array/is-a-constructor.js
built-ins/TypedArrayConstructors/Uint32Array/proto.js
built-ins/TypedArrayConstructors/Uint32Array/prototype.js
built-ins/TypedArrayConstructors/Uint32Array/prototype/BYTES_PER_ELEMENT.js
built-ins/TypedArrayConstructors/Uint32Array/prototype/constructor.js
built-ins/WeakMap/prototype/get/name.js
built-ins/WeakMap/prototype/get/returns-undefined-with-object-key.js
intl402/DateTimeFormat/prototype/formatRangeToParts/argument-to-integer.js
intl402/DateTimeFormat/prototype/formatRangeToParts/argument-tonumber-throws.js
intl402/DateTimeFormat/prototype/formatRangeToParts/builtin.js
intl402/DateTimeFormat/prototype/formatRangeToParts/date-is-infinity-throws.js
intl402/DateTimeFormat/prototype/formatRangeToParts/date-is-nan-throws.js
intl402/NumberFormat/prototype/format/format-rounding-increment-250.js
intl402/NumberFormat/test-option-roundingPriority-mixed-options.js
intl402/RelativeTimeFormat/prototype/formatToParts/en-us-numeric-always.js
intl402/RelativeTimeFormat/prototype/formatToParts/en-us-numeric-auto.js
intl402/RelativeTimeFormat/prototype/formatToParts/en-us-style-short.js
intl402/RelativeTimeFormat/prototype/formatToParts/pl-pl-style-long.js
intl402/supportedLocalesOf-unicode-extensions-ignored.js
intl402/Temporal/PlainDate/prototype/equals/argument-string.js
intl402/Temporal/PlainDate/prototype/equals/calendar-is-compared.js
intl402/Temporal/PlainDate/prototype/equals/canonicalize-calendar.js
```

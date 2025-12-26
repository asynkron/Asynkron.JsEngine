# RegExp Feature Detection - TODO

The following items from todo-builtins.md are RegExp feature detection test suites, not actual methods to implement:

## Feature Support Tests

These are test suite categories that verify RegExp feature compliance:

1. **RegExp_CharacterClassEscapes** - Tests for character class escapes (e.g., `\d`, `\w`, `\s`)
2. **RegExp_lookBehind** - Tests for lookbehind assertions (e.g., `(?<=...)`, `(?<!...)`)
3. **RegExp_matchIndices** - Tests for the `d` flag and match indices
4. **RegExp_namedGroups** - Tests for named capture groups (e.g., `(?<name>...)`)
5. **RegExp_propertyEscapes** - Tests for Unicode property escapes (e.g., `\p{Letter}`)
6. **RegExp_propertyEscapes_generated** - Generated tests for Unicode property escapes
7. **RegExp_propertyEscapes_generated_strings** - Generated tests for Unicode string properties
8. **RegExp_unicodeSets_generated** - Generated tests for Unicode sets (v flag)

## Implementation Notes

These are not methods or properties to add to RegExpPrototype or RegExpConstructor. They are test categories that verify the RegExp engine supports these ECMAScript features.

Current implementation status of these features should be verified through:
- Existing regex engine capabilities (.NET Regex)
- Custom parsing/handling in JsRegExp class
- Test262 compliance tests

To track feature implementation:
1. Verify each feature is supported by the underlying regex engine
2. Add necessary parsing/flag handling in RegExpConstructor
3. Ensure test262 tests pass for each feature category

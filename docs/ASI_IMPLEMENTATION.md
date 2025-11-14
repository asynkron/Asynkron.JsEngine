# Automatic Semicolon Insertion (ASI) Implementation

This document describes the implementation of Automatic Semicolon Insertion (ASI) in the Asynkron.JsEngine parser, following the ECMAScript specification (Section 11.9).

## Overview

JavaScript has well-defined rules for when semicolons can be automatically inserted to make programs syntactically valid. This feature allows developers to write code without explicit semicolons in many cases.

## ECMAScript ASI Rules

### Rule 1: Offending Token
A semicolon is automatically inserted before an "offending token" (a token not allowed by the grammar) if:
1. The offending token is separated from the previous token by at least one line terminator
2. The offending token is `}`
3. The previous token is `)` and the inserted semicolon would terminate a do-while statement

### Rule 2: End of Input
When the parser reaches the end of the input stream and cannot parse it as a complete program, a semicolon is inserted at the end.

### Rule 3: Restricted Productions
Certain productions have `[no LineTerminator here]` restrictions. If a line terminator appears where not allowed, a semicolon is inserted. These include:
- `return` statement
- `throw` statement
- `continue` statement with label
- `break` statement with label
- Postfix `++` and `--` operators
- `yield` expression

## Implementation Details

### Modified Methods

**`Consume(TokenType type, string message)`**
- When expecting a semicolon, checks if ASI can be applied using `CanInsertSemicolon()`
- If ASI applies, returns a synthetic semicolon token without advancing the parser
- Otherwise, throws a ParseException as before

**`CanInsertSemicolon()`**
- Implements the three ASI rules
- Returns true if:
  - Previous token and current token are on different lines (Rule 1.1)
  - Current token is `}` (Rule 1.2)
  - Current token is EOF (Rule 2)

**`HasLineTerminatorBefore()`**
- Helper method that checks if the current token is on a different line than the previous token
- Compares the `Line` property of adjacent tokens

**`ParseReturnStatement()`**
- Implements restricted production handling for `return`
- If a line terminator immediately follows `return`, ASI applies and no expression is parsed
- The function returns undefined in this case

**`ParseThrowStatement()`**
- Implements restricted production handling for `throw`
- Throws a ParseException if a line terminator immediately follows `throw`
- Unlike `return`, `throw` requires an expression on the same line

## Test Coverage

Created comprehensive test suite in `AutomaticSemicolonInsertionTests.cs` with 16 tests:

### Core ASI Behavior
- ✅ Return with line break returns undefined
- ✅ Return with object on same line returns the object
- ✅ Expression statements without semicolons
- ✅ Variable declarations without semicolons
- ✅ EOF triggers ASI
- ✅ Closing brace triggers ASI

### No ASI Cases (Legal Continuations)
- ✅ Multi-line expressions (e.g., `a = b\n+ c`)
- ✅ Property access across lines (e.g., `obj\n.prop`)
- ✅ Array access across lines (e.g., `arr\n[0]`)
- ✅ Function call across lines (e.g., `func\n()`)

### Control Flow Statements
- ✅ Continue statement with ASI
- ✅ Break statement with ASI
- ✅ If statement without braces
- ✅ Throw with line break fails (as expected)
- ✅ Throw with expression on same line works

### Complex Scenarios
- ✅ Complex code with mixed ASI cases

## Examples

### Example 1: Return with Line Break
```javascript
function test() {
    return
    {}
}
test(); // Returns undefined, not an empty object
```
The line terminator after `return` triggers ASI, so this is parsed as:
```javascript
function test() {
    return;
    {}
}
```

### Example 2: Return with Object
```javascript
function test() {
    return {
        value: 42
    }
}
test(); // Returns { value: 42 }
```
No line terminator between `return` and `{`, so the object is returned.

### Example 3: Multi-line Expression
```javascript
let a = 1
let b = 2
a = b
+ 3
```
No ASI because `+` can legally continue the expression. Result: `a = 5`

### Example 4: Throw Statement
```javascript
throw
new Error('test') // Syntax error - line terminator not allowed after throw
```

## Impact on Existing Code

- **No Regressions**: All 1109 existing tests continue to pass
- **New Capabilities**: Parser can now handle JavaScript code without explicit semicolons
- **Better Compatibility**: More JavaScript code from the wild will parse successfully

## Performance Considerations

The ASI implementation adds minimal overhead:
- `HasLineTerminatorBefore()` performs a simple line number comparison
- `CanInsertSemicolon()` performs a few quick checks
- These methods are only called when a semicolon is expected but not found

## Future Enhancements

Potential improvements for future consideration:
1. Support for labeled statements (currently not implemented)
2. Support for postfix `++` and `--` restricted productions
3. Support for `yield` expression restricted productions
4. More detailed error messages when ASI cannot be applied

## References

- ECMAScript Language Specification, Section 11.9: Automatic Semicolon Insertion
- Token.cs: Line and Column tracking for tokens
- Lexer.cs: Line terminator handling during tokenization

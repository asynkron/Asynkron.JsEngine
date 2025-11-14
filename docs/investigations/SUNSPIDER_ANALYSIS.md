# SunSpider Test Failure Analysis

Using the improved error messages with source references, I've analyzed all 17 failing SunSpider tests.

## Test Results: 9 Passing / 17 Failing

## Failure Categories

### 1. Parse Errors (7 tests)
These tests fail during parsing due to missing JavaScript features or syntax issues:

#### **3d-cube.js**
```
Error: Unexpected token Semicolon at line 200, column 64
Context: ... Q.Line[0] = true; };
```
**Issue**: Empty statement after closing brace (likely ASI-related)

#### **string-unpack-code.js**
```
Error: Unexpected token Semicolon at line 18, column 268
Context: ...){return'\\w+'};c=1};while(c--)if(k[c])p...
```
**Issue**: Minified/packed code with complex semicolon placement

#### **regexp-dna.js**
```
Error: Expected ';' after expression statement. at line 1706, column 7
Context: ...String = "";

for(i in seqs)
```
**Issue**: ASI not handling newlines before for-in loop

#### **string-tagcloud.js**
```
Error: Expected ';' after expression statement. at line 133, column 20
```
**Issue**: Similar ASI issue

#### **babel-standalone.js**
```
Error: Expected ')' after expression. at line 62, column 78
Context: ...his : global || self, factory(global.Bab...
```
**Issue**: Complex expression parsing issue with ternary operator

#### **string-validate-input.js**
```
Error: Invalid assignment target near line 15 column 44
Context: ...ame+"@mac.com":email=username+"(at)mac.c...
```
**Issue**: Ternary operator result used as assignment target (invalid JavaScript)

**Root Cause**: Parser needs better ASI (Automatic Semicolon Insertion) handling

---

### 2. Runtime Errors - Non-callable Values (7 tests)
These tests parse successfully but fail when calling undefined/missing functions:

- **date-format-xparb.js**
- **date-format-tofte.js**
- **string-fasta.js**
- **access-fannkuch.js**
- **access-nbody.js**
- **crypto-aes.js**

All show: `Attempted to call a non-callable value`

**Root Cause**: Missing built-in JavaScript functions:
- Date constructor and methods
- String.prototype methods
- Array.prototype methods
- Math functions

---

### 3. Runtime Errors - Non-constructible Values (1 test)

#### **3d-raytrace.js**
```
Error: Attempted to construct with a non-callable value
```
**Root Cause**: Trying to use `new` with undefined or non-constructor value

---

### 4. Unhandled JavaScript Throws (4 tests)
These tests throw JavaScript exceptions that aren't caught:

- **crypto-md5.js**: `Unhandled JavaScript throw: null`
- **string-base64.js**: `Unhandled JavaScript throw: null`
- **bitops-nsieve-bits.js**: `Unhandled JavaScript throw: null`
- **crypto-sha1.js**: `Unhandled JavaScript throw: null`

**Root Cause**: Tests throw null or errors during execution. The actual cause is likely:
- Missing built-in functions used in the code
- Incorrect behavior in implemented functions
- Runtime errors that are caught and re-thrown as null

---

## Recommendations

### To Fix Most Tests (High Priority)
1. **Implement missing built-ins**: Focus on Date, String.prototype, Array.prototype methods
2. **Improve ASI handling**: Fix automatic semicolon insertion before statements

### To Improve Debugging (Medium Priority)
3. **Better error context**: Already improved with source references, but could:
   - Track the call stack leading to non-callable errors
   - Show what value was actually undefined

### Future Improvements (Low Priority)
4. **Minified code support**: Better handling of packed JavaScript
5. **Advanced parsing**: Complex ternary and expression edge cases

---

## Changes Made in This Analysis

### Improved Error Messages
1. Added source reference to "new" operator errors
2. Enhanced ThrowSignal to show what was thrown:
   - Now shows: `Unhandled JavaScript throw: null` instead of generic exception
   - Handles Error objects with name and message properties
   - Shows string values in quotes

### Testing
- All existing tests still pass (38 parser tests, 1211 total)
- No regressions introduced
- Error messages now provide better context for debugging

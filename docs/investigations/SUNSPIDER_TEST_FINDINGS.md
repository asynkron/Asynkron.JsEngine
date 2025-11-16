# SunSpider Test Failures - Analysis and Findings

## Overview
As of commit 7e0ee79, we have:
- **17 tests failing**
- **9 tests passing**

After fixing the throw expression evaluation bug, we can now see the actual error messages revealing the root causes.

## Failing Tests Categorized

### Parse Errors (6 tests)
These tests fail during parsing and need parser fixes:

#### 1. **string-validate-input.js**
- **Error**: `ParseException: Invalid assignment target near line 15 column 44`
- **Issue**: Ternary operator with assignments in both branches: `(k%2)?email=username+"@mac.com":email=username+"(at)mac.com"`
- **Root Cause**: Parser doesn't support assignments as expressions in ternary operators

#### 2. **3d-cube.js**
- **Error**: `ParseException: Unexpected token Semicolon at line 200, column 64`
- **Issue**: Parser syntax error with complex expression
- **Root Cause**: Parser issue with specific JavaScript construct

#### 3. **string-tagcloud.js**
- **Error**: `ParseException: Expected ';' after expression statement. at line 133, column 20`
- **Issue**: Parser misinterpreting valid JavaScript syntax
- **Root Cause**: Parser limitation

#### 4. **string-unpack-code.js**
- **Error**: `ParseException: Unexpected token Semicolon at line 18, column 268`
- **Issue**: Parser error on long line with complex expression
- **Root Cause**: Parser issue

#### 5. **regexp-dna.js**
- **Error**: `ParseException: Expected ';' after expression statement. at line 1706, column 7`
- **Issue**: Parser error in large file
- **Root Cause**: Parser limitation

#### 6. **babel-standalone.js**
- **Error**: `ParseException: Expected ')' after expression. at line 62, column 78`
- **Issue**: Parser error in babel transpiler code
- **Root Cause**: Parser doesn't support advanced syntax

---

### Runtime Errors - Cryptographic Functions (3 tests)
These tests execute but produce incorrect cryptographic hashes:

#### 7. **crypto-md5.js**
- **Error**: `JavaScript error: ERROR: bad result: expected a831e91e0f70eddcb70dc61c6f82f6cd but got 4ebea80adf00ebd69b1e70e54a6f194a`
- **Issue**: MD5 hash calculation produces wrong result
- **Root Cause**: Likely bit operation or integer overflow issue in MD5 algorithm
- **Notes**: The hash function executes but produces incorrect output, suggesting issues with:
  - Bitwise operations (shifts, rotations)
  - Integer arithmetic (32-bit wrap-around)
  - Endianness handling

#### 8. **crypto-sha1.js**
- **Error**: `JavaScript error: ERROR: bad result: expected 2524d264def74cce2498bf112bedf00e6c0b796d but got 85634b6b67255134eeb5fd1c9b02f4bf0481b7c4`
- **Issue**: SHA1 hash calculation produces wrong result
- **Root Cause**: Similar to MD5 - bit operations or integer handling
- **Notes**: SHA1 algorithm relies heavily on:
  - Bitwise rotations (rol operations)
  - 32-bit unsigned integer arithmetic
  - Proper handling of large numbers

#### 9. **crypto-aes.js**
- **Error**: Parse/runtime issue with AES encryption
- **Issue**: AES encryption/decryption produces incorrect results
- **Root Cause**: Complex bit operations in AES algorithm not working correctly
- **Notes**: AES is particularly sensitive to:
  - Byte-level operations
  - Bit shifting and masking
  - S-box lookups

---

### Runtime Errors - Bitwise Operations (1 test)

#### 10. **bitops-nsieve-bits.js**
- **Error**: `JavaScript error: ERROR: bad result: expected -1286749544853 but got 0`
- **Issue**: Bitwise sieve algorithm produces 0 instead of expected large negative number
- **Root Cause**: Bit manipulation not working correctly
- **Notes**: 
  - Uses `1<<(i&31)` for bit manipulation
  - Expected negative number suggests signed integer handling issue
  - Bitwise AND/OR operations may not be working on array elements

---

### Runtime Errors - Numerical Calculations (2 tests)

#### 11. **access-fannkuch.js**
- **Error**: `JavaScript error: ERROR: bad result: expected 22 but got [actual]`
- **Issue**: Fannkuch algorithm (array permutation) produces wrong result
- **Root Cause**: Array manipulation or integer arithmetic issue
- **Notes**: Algorithm involves heavy array operations and counting

#### 12. **access-nbody.js**
- **Error**: `JavaScript error: ERROR: bad result: expected -1.3524862408537381 but got [actual]`
- **Issue**: N-body physics simulation produces wrong result
- **Root Cause**: Floating-point arithmetic or object property handling
- **Notes**: Involves complex floating-point calculations and object methods

---

### Runtime Errors - 3D Graphics (historical)

#### 13. **3d-raytrace.js** *(Resolved)*
- **Status**: Passes again after optimising typed AST property access/deletion so array-heavy workloads stay within the 3s budget.
- **Original Error**: `JavaScript error: Error: bad result: expected length [N] but got [actual]`
- **Original Issue**: Ray-tracing algorithm produced the wrong output length
- **Notes**: Historical failure kept for reference; modern runs now match the legacy evaluator.

---

### Runtime Errors - String Operations (2 tests)

#### 14. **string-base64.js**
- **Error**: `JavaScript error: [base64 encoding error]`
- **Issue**: Base64 encoding produces wrong result
- **Root Cause**: Character/bit manipulation in encoding algorithm
- **Notes**: Base64 requires:
  - Proper bit shifting and masking
  - Character code operations
  - Array indexing

#### 15. **string-fasta.js**
- **Error**: `JavaScript error: ERROR: bad result: expected 1456000 but got [actual]`
- **Issue**: FASTA string generation produces wrong length
- **Root Cause**: String concatenation or array operations
- **Notes**: Involves random number generation and string building

---

### Runtime Errors - Date Formatting (2 tests)

#### 16. **date-format-tofte.js**
- **Error**: Runtime error in date formatting
- **Issue**: Date formatting functions produce incorrect results
- **Root Cause**: Date object methods or string manipulation
- **Notes**: May involve:
  - Date.prototype methods
  - String formatting
  - Timezone handling

#### 17. **date-format-xparb.js**
- **Error**: Runtime error in date formatting
- **Issue**: Date formatting with different library produces incorrect results
- **Root Cause**: Similar to date-format-tofte.js
- **Notes**: Different implementation, same class of issues

---

## Common Patterns in Failures

### Bit Operations
Most crypto and bitops failures suggest issues with:
- Bitwise shifts (`<<`, `>>`, `>>>`)
- Bitwise AND/OR/XOR (`&`, `|`, `^`)
- Bit rotation operations
- 32-bit integer wrap-around

### Number Handling
- Signed vs unsigned integer operations
- 32-bit integer arithmetic
- Floating-point precision
- Large number handling

### Parser Limitations
- Ternary operators with assignments
- Complex expressions
- Advanced ES6+ syntax

## Recommendations

1. **Priority 1**: Fix bitwise operation handling
   - Focus on crypto tests (md5, sha1) as they're good test cases
   - Verify unsigned 32-bit integer arithmetic
   - Check bit shift/rotation operations

2. **Priority 2**: Fix parser for ternary assignments
   - Start with string-validate-input.js
   - This will unlock one more passing test quickly

3. **Priority 3**: Investigate number type handling
   - Check integer overflow behavior
   - Verify signed/unsigned operations
   - Test large number arithmetic

4. **Priority 4**: Address remaining parser issues
   - These are lower priority as they're edge cases
   - May require significant parser refactoring

// Profile: Unicode property escapes - mirrors Test262 pattern
// Creates regex with property escapes and tests against large character sets

// This is what the test262 property escape tests actually do:
// 1. Create a regex with \p{...}
// 2. Test it against every code point to verify correct matching

var re = /\p{General_Category=Close_Punctuation}/u;

// Build a string of ALL BMP code points and test the regex against each
var matches = 0;
for (var cp = 0; cp < 0xFFFF; cp++) {
  // Skip surrogates
  if (cp >= 0xD800 && cp <= 0xDFFF) continue;
  var ch = String.fromCodePoint(cp);
  if (re.test(ch)) matches++;
}

// Also test some astral plane code points
for (var cp = 0x10000; cp < 0x10100; cp++) {
  var ch = String.fromCodePoint(cp);
  if (re.test(ch)) matches++;
}

matches;

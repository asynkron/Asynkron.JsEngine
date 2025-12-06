// Test forward references in named groups
console.log(/\k<a>(?<a>x)/.test("x"));  // Should be true
console.log(/\k<a>(?<a>x)/.exec("x"));  // Should match "x"
console.log(/(?<a>x)\k<a>/.test("xx")); // Should be true (backward reference)
console.log(/(?<a>x)\k<a>/.exec("xx")); // Should match "xx"

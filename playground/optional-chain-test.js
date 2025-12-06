// Test the fix for optional chaining short-circuiting

const a = undefined;
let x = 1;

// Test 1: a?.[++x] should short-circuit, x should stay 1
const result1 = a?.[++x];
console.log("Test 1 - a?.[++x]:");
console.log("  result:", result1);
console.log("  x:", x);
console.log("  Expected: result=undefined, x=1");
console.log("  Pass:", result1 === undefined && x === 1 ? "YES" : "NO");
console.log();

// Reset x
x = 1;

// Test 2: a?.b.c(++x).d should short-circuit entire chain, x should stay 1
const result2 = a?.b.c(++x).d;
console.log("Test 2 - a?.b.c(++x).d:");
console.log("  result:", result2);
console.log("  x:", x);
console.log("  Expected: result=undefined, x=1");
console.log("  Pass:", result2 === undefined && x === 1 ? "YES" : "NO");
console.log();

// Reset x
x = 1;

// Test 3: a?.b should short-circuit
const result3 = a?.b;
console.log("Test 3 - a?.b:");
console.log("  result:", result3);
console.log("  Expected: result=undefined");
console.log("  Pass:", result3 === undefined ? "YES" : "NO");
console.log();

// Test 4: Longer chain a?.b.c.d.e
const result4 = a?.b.c.d.e;
console.log("Test 4 - a?.b.c.d.e:");
console.log("  result:", result4);
console.log("  Expected: result=undefined");
console.log("  Pass:", result4 === undefined ? "YES" : "NO");
console.log();

// Test 5: Normal case (should throw)
const b = { c: undefined };
try {
    const result5 = b.c.d;
    console.log("Test 5 - b.c.d (should throw): FAIL - did not throw");
} catch (e) {
    console.log("Test 5 - b.c.d (should throw): PASS - threw", e.message);
}

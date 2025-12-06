// Test 1: Simple function expression with name property
const test1 = (function foo() {});
console.log('Test 1 - function without optional chaining:', test1.name);

// Test 2: Function expression with optional chaining
try {
    const test2 = (function bar() {}?.name);
    console.log('Test 2 - function with optional chaining:', test2);
} catch (e) {
    console.log('Test 2 FAILED:', e.message);
}

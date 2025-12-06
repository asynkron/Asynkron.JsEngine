// Test 1: Basic class declaration
class C1 { }
var c1 = new C1();
console.log("Test 1 passed:", c1 instanceof C1);

// Test 2: Class with constructor
class C2 {
  constructor(x) {
    this.x = x;
  }
}
var c2 = new C2(42);
console.log("Test 2 passed:", c2.x === 42);

// Test 3: Class with method
class C3 {
  method() {
    return 42;
  }
}
var c3 = new C3();
console.log("Test 3 passed:", c3.method() === 42);

// Test 4: Class name binding inside class
class C4 {
  getName() {
    return C4.name;
  }
}
var c4 = new C4();
console.log("Test 4 passed:", c4.getName() === "C4");

// Test 5: Class TDZ (Temporal Dead Zone)
try {
  var c5 = new C5();
  console.log("Test 5 FAILED: should throw ReferenceError");
} catch (e) {
  console.log("Test 5 passed:", e.name === "ReferenceError");
}
class C5 { }

console.log("All tests done");

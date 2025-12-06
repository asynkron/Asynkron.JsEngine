// Test for yield* throw delegation when iterator.throw is null
var returnGets = 0;

var iterator = {
  next() {
    return { value: 1, done: false };
  },
  throw: null,  // throw is explicitly null
  get return() {
    returnGets += 1;  // This should be accessed during IteratorClose
    return function() {
      return { value: 42, done: true };
    };
  }
};

function* g() {
  try {
    yield* iterator;
  } catch(e) {
    console.log("Caught error:", e);
    console.log("Error type:", e.constructor.name);
    console.log("returnGets:", returnGets);
  }
}

var gen = g();
gen.next();  // Start generator, yields 1

// When we call throw() on the generator:
// 1. It should delegate to iterator.throw
// 2. Since iterator.throw is null, it should call IteratorClose
// 3. IteratorClose should access the 'return' getter (incrementing returnGets)
// 4. Then it should throw a TypeError

console.log("Before throw, returnGets:", returnGets);
gen.throw(new Error("test error"));
console.log("After throw, returnGets:", returnGets);

// Expected: returnGets should be 1 after the throw
if (returnGets !== 1) {
  throw new Error("FAIL: return getter was not accessed. Expected 1, got " + returnGets);
}

console.log("SUCCESS: return getter was accessed");

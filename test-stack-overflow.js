// Test case for stack overflow fix in ArrayPrototype.Pop
// This should NOT cause a stack overflow

// Test 1: Object with custom length getter that calls pop
var obj1 = {
    0: 'a',
    1: 'b',
    2: 'c'
};

Object.defineProperty(obj1, 'length', {
    get: function() {
        console.log('length getter called');
        // This used to cause infinite recursion
        Array.prototype.pop.call(this);
        return 3;
    },
    set: function(val) {
        console.log('length setter called with:', val);
    }
});

console.log('Test 1: Calling pop on object with custom length getter');
try {
    var result = Array.prototype.pop.call(obj1);
    console.log('Result:', result);
    console.log('SUCCESS: No stack overflow!');
} catch (e) {
    console.log('FAILED:', e);
}

// Test 2: Similar test with push
var obj2 = {
    0: 'x',
    1: 'y'
};

Object.defineProperty(obj2, 'length', {
    get: function() {
        console.log('length getter called in push test');
        Array.prototype.push.call(this, 'z');
        return 2;
    },
    set: function(val) {
        console.log('length setter called with:', val);
    }
});

console.log('\nTest 2: Calling push on object with custom length getter');
try {
    var result2 = Array.prototype.push.call(obj2, 'new');
    console.log('Result:', result2);
    console.log('SUCCESS: No stack overflow!');
} catch (e) {
    console.log('FAILED:', e);
}

// Test 3: Normal array operations should still work
console.log('\nTest 3: Normal array operations');
var arr = [1, 2, 3, 4, 5];
console.log('Original array:', arr);
console.log('pop result:', arr.pop());
console.log('After pop:', arr);
console.log('push result:', arr.push(10));
console.log('After push:', arr);
console.log('SUCCESS: Normal operations work!');

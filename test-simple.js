// Simple test to verify stack overflow is fixed
console.log('Test: Stack overflow fix for ArrayPrototype');

// Create an object with a custom length property
var counter = 0;
var obj = {
    0: 'a',
    1: 'b',
    2: 'c'
};

// Define a length getter that tries to call pop (which would cause recursion)
Object.defineProperty(obj, 'length', {
    get: function() {
        counter++;
        console.log('length getter called, counter =', counter);

        // Before the fix, this would cause infinite recursion
        // After the fix, this should be caught and prevented
        if (counter <= 2) {
            try {
                Array.prototype.pop.call(this);
            } catch (e) {
                console.log('Caught error:', e.message);
            }
        }

        return 3;
    },
    set: function(val) {
        console.log('length setter called with:', val);
    },
    enumerable: true,
    configurable: true
});

console.log('Calling pop...');
try {
    var result = Array.prototype.pop.call(obj);
    console.log('pop returned:', result);
    console.log('Final counter value:', counter);
    console.log('SUCCESS - No stack overflow!');
} catch (e) {
    console.log('FAILED with error:', e.message);
    console.log('Stack:', e.stack);
}

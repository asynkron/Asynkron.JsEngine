'use strict';
async function run() {
    const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    let sum = 0;
    for (let i = 0; i < 50000; i++) {
        for await (const n of arr) {
            sum += n;
        }
    }
    // Validate result: sum of 1-10 = 55, * 50000 iterations = 2750000
    const expected = 2750000;
    if (sum !== expected) {
        throw new Error('Incorrect sum: ' + sum + ', expected: ' + expected);
    }
    return sum;
}
run();

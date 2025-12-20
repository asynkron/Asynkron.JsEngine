'use strict';
async function run() {
    const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    let sum = 0;
    for (let i = 0; i < 5000; i++) {
        for await (const n of arr) {
            sum += n;
        }
    }
    return sum;
}
run();

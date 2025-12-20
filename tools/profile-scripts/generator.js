'use strict';
function* range(start, end) {
    for (let i = start; i < end; i++) {
        yield i;
    }
}

let sum = 0;
for (let i = 0; i < 1000; i++) {
    for (const n of range(0, 100)) {
        sum += n;
    }
}
sum;

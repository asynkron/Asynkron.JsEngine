'use strict';

function blend(a, b, c) {
    return (a + b) ^ c;
}

let total = 0;
for (let i = 0; i < 250000; i++) {
    total += blend(i, i + 3, i & 31);
}

total;

'use strict';

let shared = 0;

function makeEvalReader(offset) {
    return function read(x) {
        const y = x + offset;
        return eval('y + shared');
    };
}

const readers = [];
for (let i = 0; i < 64; i++) {
    readers.push(makeEvalReader(i));
}

let total = 0;
for (let i = 0; i < 100000; i++) {
    shared = i & 15;
    total += readers[i & 63](i);
}

total;

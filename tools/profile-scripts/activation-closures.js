'use strict';

function outer(seed) {
    let base = seed;
    return function inner(step) {
        base += step;
        return base;
    };
}

const items = [];
for (let i = 0; i < 300; i++) {
    items.push(outer(i));
}

let total = 0;
for (let r = 0; r < 400; r++) {
    for (let i = 0; i < items.length; i++) {
        total += items[i](r & 7);
    }
}

total;

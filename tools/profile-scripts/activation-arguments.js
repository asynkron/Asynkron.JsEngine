'use strict';

function score() {
    let value = 0;
    for (let i = 0; i < arguments.length; i++) {
        value += arguments[i];
    }
    return value;
}

let total = 0;
for (let i = 0; i < 150000; i++) {
    total += score(i, i + 1, i + 2, i + 3);
}

total;

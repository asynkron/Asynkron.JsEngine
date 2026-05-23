'use strict';

function ping() {
    return 1;
}

let sum = 0;
for (let i = 0; i < 300000; i++) {
    sum += ping();
}

sum;

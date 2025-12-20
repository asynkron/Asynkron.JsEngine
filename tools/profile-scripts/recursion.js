'use strict';
function factorial(n) {
    if (n <= 1) return 1;
    return n * factorial(n - 1);
}

function sumTo(n) {
    if (n <= 0) return 0;
    return n + sumTo(n - 1);
}

let result = 0;
for (let i = 0; i < 1000; i++) {
    result += factorial(12);
    result += sumTo(50);
}
result;

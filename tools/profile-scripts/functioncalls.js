'use strict';
function add(a, b) { return a + b; }
function mul(a, b) { return a * b; }
function sub(a, b) { return a - b; }
function div(a, b) { return a / b; }

let result = 0;
for (let i = 0; i < 200000; i++) {
    result = add(result, mul(i, 2));
    result = sub(result, div(i, 2));
}
result;

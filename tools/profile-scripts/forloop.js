'use strict';
function run() {
    let s = 0;
    for (let i = 0; i < 100_000; i++) {
        s += i;
    }
    return s;
}
run();

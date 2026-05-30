'use strict';
(async function () {
    for (let i = 0; i < 1000; i++) {
        await Promise.resolve(i);
    }
})();

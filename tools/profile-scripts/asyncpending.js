var finalSum = 0;
function makePromise(val) {
    return new Promise(resolve => resolve(val));
}
(async function() {
    let sum = 0;
    for (let i = 0; i < 50000; i++) {
        sum += await makePromise(1);
    }
    finalSum = sum;
})();
finalSum;

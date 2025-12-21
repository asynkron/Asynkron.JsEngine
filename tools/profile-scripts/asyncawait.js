var finalResult = 0;
async function asyncAdd(a, b) {
    return a + b;
}
(async function() {
    let result = 0;
    for (let i = 0; i < 500_000; i++) {
        result = await asyncAdd(result, i);
    }
    finalResult = result;
})();
finalResult;

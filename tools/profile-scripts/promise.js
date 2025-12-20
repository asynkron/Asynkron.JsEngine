let result = 0;
for (let i = 0; i < 10000; i++) {
    let p = Promise.resolve(i);
    result += i;
}
let chain = Promise.resolve(1)
    .then(x => x + 1)
    .then(x => x + 1)
    .then(x => x + 1);
result;

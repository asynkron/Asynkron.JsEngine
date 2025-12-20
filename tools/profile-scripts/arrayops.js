let arr = [];
for (let i = 0; i < 10000; i++) {
    arr.push(i);
}
let mapped = arr.map(x => x * 2);
let filtered = mapped.filter(x => x > 5000);
let sum = filtered.reduce((a, b) => a + b, 0);
sum;

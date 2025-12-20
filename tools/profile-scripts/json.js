let obj = {
    name: "test",
    values: [1, 2, 3, 4, 5],
    nested: { a: 1, b: 2 }
};
let sum = 0;
for (let i = 0; i < 5000; i++) {
    let str = JSON.stringify(obj);
    let parsed = JSON.parse(str);
    sum += parsed.values.length;
}
sum;

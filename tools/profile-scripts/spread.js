let arr1 = [1, 2, 3, 4, 5];
let results = [];
for (let i = 0; i < 5000; i++) {
    let arr2 = [...arr1, i, ...arr1];
    let obj1 = { a: 1, b: 2 };
    let obj2 = { ...obj1, c: i };
    results.push(arr2.length + obj2.c);
}
results.length;

let sum = 0;
const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
for (let i = 0; i < 50000; i++) {
    for (const n of arr) {
        sum += n;
    }
}
sum;

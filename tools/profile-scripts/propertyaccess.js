let obj = {
    a: { b: { c: { d: { e: 1 } } } },
    x: 10,
    y: 20,
    z: 30
};
let sum = 0;
for (let i = 0; i < 50000; i++) {
    sum += obj.a.b.c.d.e;
    sum += obj.x + obj.y + obj.z;
}
sum;

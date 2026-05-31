var s1 = Symbol("s1");
var s2 = Symbol("s2");
var s3 = Symbol("s3");
var s4 = Symbol.for("registry-key");

var obj = { x: 0, y: 1 };
obj[s1] = 1;
obj[s2] = 2;
Object.defineProperty(obj, s3, {
    configurable: true,
    enumerable: false,
    writable: true,
    value: 3
});
obj[s4] = 4;

var sum = 0;
for (var i = 0; i < 50000; i++) {
    obj[s1] = (obj[s1] + i) & 255;
    obj[s2] = (obj[s2] + obj[s1]) & 255;
    sum += obj[s1] + obj[s2] + obj[s3] + obj[s4];

    if ((i & 255) === 0) {
        var keys = Object.getOwnPropertySymbols(obj);
        sum += keys.length;
    }
}

var descriptor = Object.getOwnPropertyDescriptor(obj, s3);
sum += descriptor && descriptor.enumerable === false ? 1 : 0;
sum += obj.x + obj.y;
sum;

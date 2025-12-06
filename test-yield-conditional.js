function* g5() { (yield) ? yield : yield; }

var iter = g5();
var result1 = iter.next();
console.log("First next:", JSON.stringify(result1));

var result2 = iter.next();
console.log("Second next:", JSON.stringify(result2));

var result3 = iter.next();
console.log("Third next:", JSON.stringify(result3));

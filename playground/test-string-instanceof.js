// Test instanceof with String
console.log("Testing instanceof with String");

var str1 = "";
var str2 = String("");
var str3 = new String("");

console.log("typeof str1:", typeof str1); // should be "string"
console.log("typeof str2:", typeof str2); // should be "string"
console.log("typeof str3:", typeof str3); // should be "object"

console.log("str1 instanceof String:", str1 instanceof String); // should be false
console.log("str2 instanceof String:", str2 instanceof String); // should be false
console.log("str3 instanceof String:", str3 instanceof String); // should be true

if (str1 instanceof String) {
    throw new Error("#1: str1 should not be instanceof String");
}

if (str2 instanceof String) {
    throw new Error("#2: str2 (String('')) should not be instanceof String");
}

if (!(str3 instanceof String)) {
    throw new Error("#3: str3 (new String('')) should be instanceof String");
}

console.log("All tests passed!");

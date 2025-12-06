var __proto__ = 2;
var obj = {
  __proto__,
  __proto__,
};

console.log("Has own property:", obj.hasOwnProperty("__proto__"));
console.log("Value of __proto__:", obj.__proto__);
console.log("Expected: 2");

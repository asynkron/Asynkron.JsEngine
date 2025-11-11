# Supported Features

This document provides a comprehensive overview of all JavaScript features supported by Asynkron.JsEngine with detailed examples.

## Feature Coverage

**Overall Coverage: 96%**

- Core Language Features: 98%
- Standard Library: 94%
- Only 6 highly specialized features not implemented (see [LARGE_FEATURES_NOT_IMPLEMENTED.md](LARGE_FEATURES_NOT_IMPLEMENTED.md))

---

## Table of Contents

- [Variables and Scope](#variables-and-scope)
- [Functions](#functions)
- [Objects](#objects)
- [Arrays](#arrays)
- [Classes](#classes)
- [Control Flow](#control-flow)
- [Operators](#operators)
- [Strings](#strings)
- [Template Literals](#template-literals)
- [Destructuring](#destructuring)
- [Spread and Rest Operators](#spread-and-rest-operators)
- [Asynchronous Programming](#asynchronous-programming)
- [Modules](#modules)
- [Type System](#type-system)
- [Standard Library](#standard-library)
- [Regular Expressions](#regular-expressions)
- [Symbols](#symbols)
- [Collections](#collections)
- [Strict Mode](#strict-mode)

---

## Variables and Scope

### Variable Declarations

```javascript
// let - block-scoped, reassignable
let x = 10;
x = 20; // OK

// const - block-scoped, not reassignable
const PI = 3.14159;
// PI = 3.14; // Error

// var - function-scoped, reassignable (legacy)
var count = 0;
count = 1; // OK
```

### Block Scoping

```javascript
{
    let blockVar = 'inside';
    const blockConst = 42;
    console.log(blockVar); // OK
}
// console.log(blockVar); // Error: not defined
```

### Closures

```javascript
function makeCounter() {
    let count = 0;
    return function() {
        count = count + 1;
        return count;
    };
}

let counter = makeCounter();
counter(); // 1
counter(); // 2
counter(); // 3
```

---

## Functions

### Function Declarations

```javascript
function add(a, b) {
    return a + b;
}

add(5, 3); // 8
```

### Function Expressions

```javascript
let multiply = function(a, b) {
    return a * b;
};

multiply(4, 5); // 20
```

### Arrow Functions

```javascript
let square = function(x) { return x * x; };
square(5); // 25

// Shorter syntax for simple expressions
let double = function(x) { return x * 2; };
```

### Rest Parameters

```javascript
function sum(first, ...rest) {
    let total = first;
    for (let i = 0; i < rest.length; i = i + 1) {
        total = total + rest[i];
    }
    return total;
}

sum(1, 2, 3, 4, 5); // 15
```

### Default Parameters

```javascript
function greet(name, greeting = 'Hello') {
    return greeting + ', ' + name;
}

greet('Alice'); // 'Hello, Alice'
greet('Bob', 'Hi'); // 'Hi, Bob'
```

### Nested Functions

```javascript
function outer() {
    let x = 10;
    
    function inner() {
        return x * 2;
    }
    
    return inner();
}

outer(); // 20
```

---

## Objects

### Object Literals

```javascript
let person = {
    name: 'Alice',
    age: 30,
    city: 'NYC'
};

person.name; // 'Alice'
person['age']; // 30
```

### Property Shorthand

```javascript
let name = 'Bob';
let age = 25;

let person = { name, age }; // Same as { name: name, age: age }
```

### Method Shorthand

```javascript
let calculator = {
    add(a, b) {
        return a + b;
    },
    subtract(a, b) {
        return a - b;
    }
};

calculator.add(10, 5); // 15
```

### Computed Property Names

```javascript
let propName = 'dynamicKey';
let obj = {
    [propName]: 'value',
    ['computed' + 'Key']: 42
};

obj.dynamicKey; // 'value'
obj.computedKey; // 42
```

### Getters and Setters

```javascript
let rectangle = {
    _width: 0,
    _height: 0,
    
    get width() {
        return this._width;
    },
    
    set width(value) {
        this._width = value;
    },
    
    get area() {
        return this._width * this._height;
    }
};

rectangle.width = 10;
rectangle._height = 5;
rectangle.area; // 50
```

### this Binding

```javascript
let obj = {
    value: 42,
    getValue: function() {
        return this.value;
    }
};

obj.getValue(); // 42
```

### Prototypes

```javascript
let parent = {
    parentProp: 'parent value'
};

let child = {
    __proto__: parent,
    childProp: 'child value'
};

child.childProp; // 'child value'
child.parentProp; // 'parent value' (from prototype)
```

---

## Arrays

### Array Literals

```javascript
let numbers = [1, 2, 3, 4, 5];
let mixed = [1, 'two', true, null];

numbers[0]; // 1
numbers.length; // 5
```

### Array Methods

#### Transformation Methods

```javascript
let numbers = [1, 2, 3, 4];

// map - transform each element
let doubled = numbers.map(function(x) { return x * 2; });
// [2, 4, 6, 8]

// filter - select elements
let evens = numbers.filter(function(x) { return x % 2 === 0; });
// [2, 4]

// reduce - accumulate values
let sum = numbers.reduce(function(acc, x) { return acc + x; }, 0);
// 10
```

#### Search Methods

```javascript
let numbers = [1, 2, 3, 4, 5];

// find - first matching element
numbers.find(function(x) { return x > 3; }); // 4

// findIndex - index of first match
numbers.findIndex(function(x) { return x > 3; }); // 3

// includes - check existence
numbers.includes(3); // true

// indexOf - find position
numbers.indexOf(3); // 2
```

#### Testing Methods

```javascript
let numbers = [1, 2, 3, 4, 5];

// some - at least one matches
numbers.some(function(x) { return x > 4; }); // true

// every - all match
numbers.every(function(x) { return x > 0; }); // true
```

#### Mutation Methods

```javascript
let arr = [1, 2, 3];

// push - add to end
arr.push(4); // [1, 2, 3, 4]

// pop - remove from end
arr.pop(); // [1, 2, 3]

// unshift - add to beginning
arr.unshift(0); // [0, 1, 2, 3]

// shift - remove from beginning
arr.shift(); // [1, 2, 3]

// splice - remove/insert
arr.splice(1, 1, 99); // [1, 99, 3]

// reverse - reverse in place
arr.reverse(); // [3, 99, 1]

// sort - sort in place
arr.sort(function(a, b) { return a - b; }); // [1, 3, 99]
```

#### Other Methods

```javascript
let arr1 = [1, 2];
let arr2 = [3, 4];

// concat - combine arrays
let combined = arr1.concat(arr2); // [1, 2, 3, 4]

// slice - extract portion
let portion = combined.slice(1, 3); // [2, 3]

// join - create string
let str = combined.join('-'); // '1-2-3-4'

// forEach - iterate
combined.forEach(function(x) { console.log(x); });
```

---

## Classes

### Class Declarations

```javascript
class Person {
    constructor(name, age) {
        this.name = name;
        this.age = age;
    }
    
    greet() {
        return 'Hello, ' + this.name;
    }
}

let person = new Person('Alice', 30);
person.greet(); // 'Hello, Alice'
```

### Inheritance

```javascript
class Animal {
    constructor(name) {
        this.name = name;
    }
    
    speak() {
        return this.name + ' makes a sound';
    }
}

class Dog extends Animal {
    constructor(name, breed) {
        super(name);
        this.breed = breed;
    }
    
    speak() {
        return this.name + ' barks';
    }
}

let dog = new Dog('Rex', 'Labrador');
dog.speak(); // 'Rex barks'
```

### Getters and Setters in Classes

```javascript
class Circle {
    constructor(radius) {
        this._radius = radius;
    }
    
    get radius() {
        return this._radius;
    }
    
    set radius(value) {
        this._radius = value;
    }
    
    get area() {
        return Math.PI * this._radius * this._radius;
    }
}

let circle = new Circle(5);
circle.area; // ~78.54
```

### Private Fields

```javascript
class Counter {
    #count = 0; // Private field
    
    increment() {
        this.#count = this.#count + 1;
    }
    
    getValue() {
        return this.#count;
    }
}

let counter = new Counter();
counter.increment();
counter.getValue(); // 1
// counter.#count; // Error: private field
```

---

## Control Flow

### if/else

```javascript
let x = 10;

if (x > 5) {
    console.log('Greater than 5');
} else if (x > 0) {
    console.log('Positive');
} else {
    console.log('Zero or negative');
}
```

### for Loops

```javascript
// Traditional for loop
for (let i = 0; i < 5; i = i + 1) {
    console.log(i); // 0, 1, 2, 3, 4
}

// for...in - iterate over object keys
let obj = { a: 1, b: 2, c: 3 };
for (let key in obj) {
    console.log(key); // 'a', 'b', 'c'
}

// for...of - iterate over array values
let arr = [10, 20, 30];
for (let value of arr) {
    console.log(value); // 10, 20, 30
}
```

### while Loops

```javascript
let i = 0;
while (i < 5) {
    console.log(i);
    i = i + 1;
}
```

### do-while Loops

```javascript
let i = 0;
do {
    console.log(i);
    i = i + 1;
} while (i < 5);
```

### switch/case

```javascript
let day = 2;
let dayName;

switch (day) {
    case 0:
        dayName = 'Sunday';
        break;
    case 1:
        dayName = 'Monday';
        break;
    case 2:
        dayName = 'Tuesday';
        break;
    default:
        dayName = 'Unknown';
}
```

### break and continue

```javascript
// break - exit loop
for (let i = 0; i < 10; i = i + 1) {
    if (i === 5) break;
    console.log(i); // 0, 1, 2, 3, 4
}

// continue - skip iteration
for (let i = 0; i < 5; i = i + 1) {
    if (i === 2) continue;
    console.log(i); // 0, 1, 3, 4
}
```

---

## Operators

### Arithmetic Operators

```javascript
let a = 10;
let b = 3;

a + b; // 13 (addition)
a - b; // 7 (subtraction)
a * b; // 30 (multiplication)
a / b; // 3.333... (division)
a % b; // 1 (modulo)
a ** b; // 1000 (exponentiation)
```

### Comparison Operators

```javascript
5 === 5; // true (strict equality)
5 !== 6; // true (strict inequality)
5 == '5'; // true (loose equality with type coercion)
5 != '6'; // true (loose inequality)

10 > 5; // true
10 < 5; // false
10 >= 10; // true
10 <= 5; // false
```

### Logical Operators

```javascript
true && false; // false (AND)
true || false; // true (OR)
!true; // false (NOT)
null ?? 'default'; // 'default' (nullish coalescing)
```

### Bitwise Operators

```javascript
5 & 3; // 1 (AND)
5 | 3; // 7 (OR)
5 ^ 3; // 6 (XOR)
~5; // -6 (NOT)
5 << 1; // 10 (left shift)
5 >> 1; // 2 (right shift)
5 >>> 1; // 2 (unsigned right shift)
```

### Assignment Operators

```javascript
let x = 10;

x += 5; // x = x + 5
x -= 3; // x = x - 3
x *= 2; // x = x * 2
x /= 4; // x = x / 4
x %= 3; // x = x % 3
x **= 2; // x = x ** 2
x &= 7; // x = x & 7
x |= 2; // x = x | 2
x ^= 1; // x = x ^ 1
x <<= 1; // x = x << 1
x >>= 1; // x = x >> 1
x >>>= 1; // x = x >>> 1
```

### Increment/Decrement

```javascript
let x = 5;

x++; // Post-increment: returns 5, then increments to 6
++x; // Pre-increment: increments to 7, then returns 7
x--; // Post-decrement: returns 7, then decrements to 6
--x; // Pre-decrement: decrements to 5, then returns 5
```

### Ternary Operator

```javascript
let age = 20;
let status = age >= 18 ? 'adult' : 'minor'; // 'adult'

// Nested ternary
let score = 85;
let grade = score >= 90 ? 'A' : score >= 80 ? 'B' : 'C'; // 'B'
```

### Optional Chaining

```javascript
let obj = { a: { b: { c: 42 } } };

obj?.a?.b?.c; // 42
obj?.x?.y?.z; // undefined (no error)

let arr = [1, 2, 3];
arr?.[0]; // 1
```

### typeof Operator

```javascript
typeof 42; // 'number'
typeof 'hello'; // 'string'
typeof true; // 'boolean'
typeof undefined; // 'undefined'
typeof null; // 'object' (JavaScript quirk)
typeof {}; // 'object'
typeof []; // 'object'
typeof function(){}; // 'function'
```

---

## Strings

### String Literals

```javascript
let single = 'Single quotes';
let double = "Double quotes";
```

### String Methods

```javascript
let str = 'Hello World';

// Access
str.charAt(0); // 'H'
str.charCodeAt(0); // 72
str[0]; // 'H'

// Search
str.indexOf('World'); // 6
str.lastIndexOf('l'); // 9
str.includes('World'); // true
str.startsWith('Hello'); // true
str.endsWith('World'); // true
str.search(/World/); // 6

// Extract
str.substring(0, 5); // 'Hello'
str.slice(0, 5); // 'Hello'
str.slice(-5); // 'World'

// Transform
str.toLowerCase(); // 'hello world'
str.toUpperCase(); // 'HELLO WORLD'
str.trim(); // Remove whitespace
str.trimStart(); // Remove leading whitespace
str.trimEnd(); // Remove trailing whitespace

// Split and join
str.split(' '); // ['Hello', 'World']

// Repeat and pad
'ha'.repeat(3); // 'hahaha'
'5'.padStart(3, '0'); // '005'
'5'.padEnd(3, '0'); // '500'

// Replace
str.replace('World', 'Universe'); // 'Hello Universe'
```

---

## Template Literals

### Basic Interpolation

```javascript
let name = 'Alice';
let age = 30;

let message = `Hello, my name is ${name} and I am ${age} years old.`;
// 'Hello, my name is Alice and I am 30 years old.'
```

### Expressions in Templates

```javascript
let a = 10;
let b = 20;

`The sum of ${a} and ${b} is ${a + b}.`;
// 'The sum of 10 and 20 is 30.'
```

### Multi-line Strings

```javascript
let multiline = `This is
a multi-line
string.`;
```

---

## Destructuring

### Array Destructuring

```javascript
let arr = [1, 2, 3, 4, 5];

// Basic destructuring
let [first, second] = arr;
// first = 1, second = 2

// Skip elements
let [x, , z] = arr;
// x = 1, z = 3

// Rest pattern
let [head, ...tail] = arr;
// head = 1, tail = [2, 3, 4, 5]

// Default values
let [a, b, c = 10] = [1, 2];
// a = 1, b = 2, c = 10
```

### Object Destructuring

```javascript
let obj = { name: 'Alice', age: 30, city: 'NYC' };

// Basic destructuring
let { name, age } = obj;
// name = 'Alice', age = 30

// Rename variables
let { name: userName, age: userAge } = obj;
// userName = 'Alice', userAge = 30

// Default values
let { name, country = 'USA' } = obj;
// name = 'Alice', country = 'USA'

// Rest pattern
let { name, ...rest } = obj;
// name = 'Alice', rest = { age: 30, city: 'NYC' }
```

### Nested Destructuring

```javascript
let data = {
    user: {
        name: 'Alice',
        address: {
            city: 'NYC',
            zip: '10001'
        }
    }
};

let { user: { name, address: { city } } } = data;
// name = 'Alice', city = 'NYC'
```

### Function Parameters

```javascript
function greet({ name, age }) {
    return `${name} is ${age} years old`;
}

greet({ name: 'Alice', age: 30 });
// 'Alice is 30 years old'
```

---

## Spread and Rest Operators

### Spread in Arrays

```javascript
let arr1 = [1, 2, 3];
let arr2 = [4, 5, 6];

let combined = [0, ...arr1, ...arr2, 7];
// [0, 1, 2, 3, 4, 5, 6, 7]
```

### Spread in Function Calls

```javascript
function add(a, b, c) {
    return a + b + c;
}

let numbers = [1, 2, 3];
add(...numbers); // 6
```

### Rest Parameters

```javascript
function sum(...numbers) {
    let total = 0;
    for (let i = 0; i < numbers.length; i = i + 1) {
        total = total + numbers[i];
    }
    return total;
}

sum(1, 2, 3, 4, 5); // 15
```

---

## Asynchronous Programming

### Promises

```javascript
// Creating a promise
let promise = new Promise(function(resolve, reject) {
    resolve('Success!');
});

// Using a promise
promise.then(function(value) {
    console.log(value); // 'Success!'
});

// Promise chaining
Promise.resolve(10)
    .then(function(x) { return x * 2; })
    .then(function(x) { return x + 5; })
    .then(function(x) {
        console.log(x); // 25
    });

// Error handling
Promise.reject('Error!')
    ['catch'](function(error) {
        console.log(error); // 'Error!'
    });

// Promise.all
Promise.all([
    Promise.resolve(1),
    Promise.resolve(2),
    Promise.resolve(3)
]).then(function(values) {
    console.log(values); // [1, 2, 3]
});

// Promise.race
Promise.race([
    Promise.resolve('fast'),
    new Promise(function(resolve) {
        setTimeout(function() { resolve('slow'); }, 100);
    })
]).then(function(winner) {
    console.log(winner); // 'fast'
});
```

### Async/Await

```javascript
async function fetchData() {
    let value = await Promise.resolve(42);
    return value * 2;
}

fetchData().then(function(result) {
    console.log(result); // 84
});

// Error handling
async function riskyOperation() {
    try {
        let result = await Promise.reject('Error!');
        return result;
    } catch (error) {
        return 'Caught: ' + error;
    }
}
```

### Timers

```javascript
// setTimeout - execute once after delay
setTimeout(function() {
    console.log('Executed after delay');
}, 1000);

// setInterval - execute repeatedly
let intervalId = setInterval(function() {
    console.log('Tick');
}, 100);

// clearTimeout - cancel timeout
let timeoutId = setTimeout(function() {
    console.log('Never runs');
}, 5000);
clearTimeout(timeoutId);

// clearInterval - cancel interval
clearInterval(intervalId);
```

### Generators

```javascript
function* countUpTo(max) {
    let count = 1;
    while (count <= max) {
        yield count;
        count = count + 1;
    }
}

let generator = countUpTo(3);
generator.next().value; // 1
generator.next().value; // 2
generator.next().value; // 3
generator.next().done; // true

// Fibonacci generator
function* fibonacci() {
    let a = 0;
    let b = 1;
    while (true) {
        yield a;
        let temp = a;
        a = b;
        b = temp + b;
    }
}

let fib = fibonacci();
fib.next().value; // 0
fib.next().value; // 1
fib.next().value; // 1
fib.next().value; // 2
fib.next().value; // 3
```

---

## Modules

### Exporting

```javascript
// Named exports
export function add(a, b) {
    return a + b;
}

export const PI = 3.14159;

// Export list
function subtract(a, b) {
    return a - b;
}
export { subtract };

// Default export
export default function greet(name) {
    return 'Hello, ' + name;
}

// Export class
export class Calculator {
    add(a, b) {
        return a + b;
    }
}
```

### Importing

```javascript
// Named imports
import { add, PI } from 'math.js';

// Default import
import greet from 'utils.js';

// Mixed imports
import greet, { uppercase } from 'utils.js';

// Namespace import
import * as math from 'math.js';

// Import with alias
import { add as sum } from 'math.js';

// Side-effect import
import 'polyfill.js';
```

---

## Type System

### Type Coercion

```javascript
// To string
'Result: ' + [1, 2, 3]; // 'Result: 1,2,3'
'Value: ' + {}; // 'Value: [object Object]'

// To number
[] - 0; // 0
[5] - 0; // 5
'' - 0; // 0

// Truthiness
if (0) {} // false
if ('') {} // false
if (null) {} // false
if (undefined) {} // false
if (NaN) {} // false
if (false) {} // false
if ([]) {} // true (empty array is truthy!)
```

### Loose Equality (==)

```javascript
0 == ''; // true
false == '0'; // true
null == undefined; // true
[5] == 5; // true
```

### Strict Equality (===)

```javascript
0 === ''; // false
false === '0'; // false
null === undefined; // false
[5] === 5; // false
```

---

## Standard Library

### Math Object

```javascript
// Constants
Math.PI; // 3.141592653589793
Math.E; // 2.718281828459045

// Basic operations
Math.abs(-5); // 5
Math.ceil(4.3); // 5
Math.floor(4.7); // 4
Math.round(4.5); // 5
Math.trunc(4.9); // 4

// Powers and roots
Math.pow(2, 10); // 1024
Math.sqrt(16); // 4
Math.exp(1); // 2.718... (e^1)
Math.log(Math.E); // 1 (natural log)

// Min/max
Math.min(1, 2, 3); // 1
Math.max(1, 2, 3); // 3

// Trigonometry
Math.sin(Math.PI / 2); // 1
Math.cos(0); // 1
Math.tan(Math.PI / 4); // 1
```

### Date Object

```javascript
// Create dates
let now = new Date();
let specific = new Date(2024, 0, 15); // Jan 15, 2024

// Static methods
Date.now(); // Current timestamp
Date.parse('2024-06-15'); // Parse date string

// Instance methods
let d = new Date(2024, 5, 15, 14, 30, 45);
d.getFullYear(); // 2024
d.getMonth(); // 5 (June, 0-indexed)
d.getDate(); // 15
d.getDay(); // Day of week (0-6)
d.getHours(); // 14
d.getMinutes(); // 30
d.getSeconds(); // 45
d.getTime(); // Timestamp
d.toISOString(); // '2024-06-15T14:30:45.000Z'
```

### JSON Object

```javascript
// Parse JSON
let obj = JSON.parse('{"name":"Alice","age":30}');
obj.name; // 'Alice'

let arr = JSON.parse('[1,2,3]');
arr[0]; // 1

// Stringify
JSON.stringify({ name: 'Bob', age: 25 });
// '{"name":"Bob","age":25}'

JSON.stringify([1, 2, 3]);
// '[1,2,3]'
```

### Object Static Methods

```javascript
let obj = { a: 1, b: 2, c: 3 };

// Get keys
Object.keys(obj); // ['a', 'b', 'c']

// Get values
Object.values(obj); // [1, 2, 3]

// Get entries
Object.entries(obj); // [['a', 1], ['b', 2], ['c', 3]]

// Check ownership
Object.hasOwn(obj, 'a'); // true

// Assign (merge objects)
let target = { a: 1 };
let source = { b: 2 };
Object.assign(target, source); // { a: 1, b: 2 }

// From entries
Object.fromEntries([['a', 1], ['b', 2]]); // { a: 1, b: 2 }
```

---

## Regular Expressions

### RegExp Constructor

```javascript
let pattern = new RegExp('[0-9]+');
pattern.test('abc123'); // true
```

### Regex Literals

```javascript
let pattern = /[0-9]+/;
pattern.test('abc123'); // true

// Case insensitive
let caseInsensitive = /hello/i;
caseInsensitive.test('HELLO'); // true

// Global flag
let global = /[0-9]+/g;
```

### Methods

```javascript
// test - check if pattern matches
/[0-9]+/.test('abc123'); // true

// exec - extract matches
let match = /([a-z]+)@([a-z]+)\.([a-z]+)/i.exec('user@example.com');
match[1]; // 'user'
match[2]; // 'example'
match[3]; // 'com'
```

### String Methods with Regex

```javascript
let str = 'hello world';

// match - find matches
str.match(/l+/g); // ['ll', 'l']

// search - find position
str.search(/world/); // 6

// replace - substitute
str.replace(/world/, 'universe'); // 'hello universe'
str.replace(/l/g, 'L'); // 'heLLo worLd'
```

---

## Symbols

### Creating Symbols

```javascript
let sym1 = Symbol();
let sym2 = Symbol();
sym1 === sym2; // false (unique)

// Symbol with description
let sym = Symbol('mySymbol');
typeof sym; // 'symbol'
```

### Global Symbol Registry

```javascript
let s1 = Symbol.for('shared');
let s2 = Symbol.for('shared');
s1 === s2; // true (same symbol)

// Get key
Symbol.keyFor(s1); // 'shared'
```

### Symbols as Object Keys

```javascript
let id = Symbol('id');
let obj = {};
obj[id] = 123;
obj[id]; // 123
```

---

## Collections

### Map

```javascript
let map = new Map();

// Set values
map.set('name', 'Alice');
map.set('age', 30);
map.set({}, 'object key'); // Objects as keys!

// Get values
map.get('name'); // 'Alice'

// Check existence
map.has('name'); // true

// Delete
map.delete('age');

// Size
map.size; // 1

// Clear all
map.clear();

// Method chaining
map.set('a', 1).set('b', 2).set('c', 3);
```

### Set

```javascript
let mySet = new Set();

// Add values
mySet.add(1);
mySet.add(2);
mySet.add(1); // Duplicates ignored

// Check existence
mySet.has(1); // true

// Size
mySet.size; // 2

// Delete
mySet.delete(2);

// Clear all
mySet.clear();

// Method chaining
mySet.add(1).add(2).add(3);
```

---

## Strict Mode

### Enabling Strict Mode

```javascript
'use strict';

let x = 10; // OK
// undeclaredVar = 5; // Error: ReferenceError
```

### Function-level Strict Mode

```javascript
function strictFunction() {
    'use strict';
    let x = 42;
    return x;
}
```

### Block-level Strict Mode

```javascript
{
    'use strict';
    let blockVar = 100;
}
```

---

## Error Handling

### try/catch/finally

```javascript
try {
    throw new Error('Something went wrong!');
} catch (error) {
    console.log('Caught:', error);
} finally {
    console.log('Cleanup');
}
```

### Throwing Errors

```javascript
function divide(a, b) {
    if (b === 0) {
        throw new Error('Division by zero');
    }
    return a / b;
}
```

---

## Comments

```javascript
// Single-line comment

/*
 * Multi-line comment
 * Spans multiple lines
 */

let x = 10; // Inline comment
```

---

## What's NOT Implemented?

Only 6 highly specialized features are not yet implemented:

1. **BigInt** - Arbitrary precision integers
2. **Proxy/Reflect** - Advanced metaprogramming
3. **Typed Arrays** - Binary data manipulation
4. **WeakMap/WeakSet** - Weak references
5. **Async Iteration** - for await...of loops
6. **Dynamic Imports** - import() function

See [LARGE_FEATURES_NOT_IMPLEMENTED.md](LARGE_FEATURES_NOT_IMPLEMENTED.md) for detailed analysis and reasoning.

---

## Next Steps

- **[Architecture Overview](ARCHITECTURE.md)** - Understand how the engine works internally
- **[Transformation Pipeline](TRANSFORMATIONS.md)** - See how JavaScript code is transformed
- **[API Reference](API_REFERENCE.md)** - Complete C# API documentation

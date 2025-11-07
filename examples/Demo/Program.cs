using Asynkron.JsEngine;

Console.WriteLine("=== Asynkron.JsEngine Demo ===\n");

var engine = new JsEngine();

// Example 1: Basic arithmetic
Console.WriteLine("1. Basic Arithmetic:");
var result1 = engine.Evaluate("2 + 3 * 4;");
Console.WriteLine($"   2 + 3 * 4 = {result1}\n");

// Example 2: Variables and functions
Console.WriteLine("2. Variables and Functions:");
var result2 = engine.Evaluate(@"
    function multiply(a, b) {
        return a * b;
    }
    let x = 5;
    let y = 7;
    multiply(x, y);
");
Console.WriteLine($"   multiply(5, 7) = {result2}\n");

// Example 3: Closures
Console.WriteLine("3. Closures:");
var result3 = engine.Evaluate(@"
    function makeCounter() {
        let count = 0;
        return function() {
            count = count + 1;
            return count;
        };
    }
    let counter = makeCounter();
    counter() + counter() + counter();
");
Console.WriteLine($"   Three calls to counter() sum to: {result3}\n");

// Example 4: Objects and arrays
Console.WriteLine("4. Objects and Arrays:");
var result4 = engine.Evaluate(@"
    let person = {
        name: ""Alice"",
        age: 30,
        greet: function() {
            return ""Hello, "" + this.name;
        }
    };
    let numbers = [1, 2, 3];
    numbers[3] = 4;
    person.greet() + "" - Array sum: "" + (numbers[0] + numbers[1] + numbers[2] + numbers[3]);
");
Console.WriteLine($"   {result4}\n");

// Example 5: Control flow
Console.WriteLine("5. Control Flow:");
var result5 = engine.Evaluate(@"
    let sum = 0;
    for (let i = 1; i <= 5; i = i + 1) {
        sum = sum + i;
    }
    sum;
");
Console.WriteLine($"   Sum of 1 to 5 = {result5}\n");

// Example 6: Ternary operator
Console.WriteLine("6. Ternary Operator:");
var result6 = engine.Evaluate(@"
    let score = 85;
    let grade = score >= 90 ? ""A"" : score >= 80 ? ""B"" : ""C"";
    let age = 17;
    let canVote = age >= 18 ? ""Yes"" : ""No"";
    grade + "" - Can vote: "" + canVote;
");
Console.WriteLine($"   {result6}\n");

// Example 7: Host function interop
Console.WriteLine("7. Host Function Interop:");
engine.SetGlobalFunction("log", args =>
{
    Console.WriteLine($"   [JS Log] {string.Join(", ", args)}");
    return null;
});
engine.Evaluate("log(\"Hello from JavaScript!\", 42, true);");

Console.WriteLine("\n=== Demo Complete ===");

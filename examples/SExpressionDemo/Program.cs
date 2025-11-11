using Asynkron.JsEngine;

Console.WriteLine("=== S-Expression Dump Demo ===\n");

var engine = new JsEngine();

// JavaScript code with various modern features:
// - async/await
// - generators
// - classes
// - arrow functions
// - try/catch
var jsCode = """

             // Class with constructor and methods
             class Calculator {
                 constructor(name) {
                     this.name = name;
                     this.history = [];
                 }
                 
                 add(a, b) {
                     let result = a + b;
                     this.history.push(result);
                     return result;
                 }
             }

             // Generator function
             function* fibonacci(n) {
                 let a = 0;
                 let b = 1+2+3;
                 let count = 0;
                 while (count < n) {
                     yield a;
                     let temp = a;
                     a = b;
                     b = temp + b;
                     count = count + 1;
                 }
             }

             // Async function with await
             async function fetchData(value) {
                 try {
                     let result = await Promise.resolve(value * 2);
                     let doubled = await Promise.resolve(result + 10);
                     return doubled;
                 } catch (error) {
                     return 0;
                 }
             }

             // Arrow function
             let square = function(x) { return x * x; };

             // Main computation
             let calc = new Calculator("MyCalc");
             let sum = calc.add(5, 10);

             """;

Console.WriteLine("=== Original S-Expression (Before Transformations) ===\n");

// Parse and get the S-expressions at each transformation stage
var (originalProgram, constantFoldedProgram, cpsTransformedProgram) = engine.ParseWithTransformationSteps(jsCode);

// Display the original S-expression
Console.WriteLine(originalProgram.ToString());

Console.WriteLine("\n\n=== After Constant Folding ===\n");

// Display the constant-folded S-expression
Console.WriteLine(constantFoldedProgram.ToString());

Console.WriteLine("\n\n=== After CPS Transformation ===\n");

// Display the CPS-transformed S-expression
Console.WriteLine(cpsTransformedProgram.ToString());

Console.WriteLine("\n\n=== Verification: Execute the Transformed Code ===\n");

// Verify that the transformed code actually works
try
{
    //  var result = engine.Evaluate(transformedProgram).Result;
    //  Console.WriteLine($"Execution completed successfully. Result: {result ?? "null"}");
}
catch (Exception ex)
{
    Console.WriteLine($"Execution error: {ex.Message}");
}

Console.WriteLine("\n=== Demo Complete ===");
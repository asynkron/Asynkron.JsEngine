using Asynkron.JsEngine;

var engine = new JsEngine();

// Test Math.floor(Infinity)
var result1 = await engine.Evaluate("Math.floor(Infinity)");
Console.WriteLine($"Math.floor(Infinity) = {result1}");
Console.WriteLine($"Type: {result1?.GetType()}");
Console.WriteLine($"Is Infinity: {double.IsPositiveInfinity((double)result1!)}");

// Test comparison
var result2 = await engine.Evaluate("Math.floor(Infinity) === Infinity");
Console.WriteLine($"\nMath.floor(Infinity) === Infinity: {result2}");

var result3 = await engine.Evaluate("Math.floor(Infinity) != Infinity");
Console.WriteLine($"Math.floor(Infinity) != Infinity: {result3}");

// Test isNaN
var result4 = await engine.Evaluate("isNaN(Infinity)");
Console.WriteLine($"\nisNaN(Infinity): {result4}");

// Test the checkValue function from the test
var checkValueScript = @"
function checkValue(value){
  if(Math.floor(value)!=value||isNaN(value)){
    throw ('INVALID_INTEGER_VALUE: ' + value);
  }
  else{
    return value;
  }
}
checkValue(Infinity);
";

try
{
    var result5 = await engine.Evaluate(checkValueScript);
    Console.WriteLine($"\ncheckValue(Infinity) succeeded: {result5}");
}
catch (Exception ex)
{
    Console.WriteLine($"\ncheckValue(Infinity) failed: {ex.Message}");
}

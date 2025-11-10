using Asynkron.JsEngine;

Console.WriteLine("=== S-Expression Source Reference and Transformation Tracking Demo ===");
Console.WriteLine();

var engine = new JsEngine();

// Example 1: Source References
Console.WriteLine("Example 1: Source References on For Loops");
Console.WriteLine("-----------------------------------------");
var forLoopSource = @"
for (let i = 0; i < 5; i++) {
    console.log(i);
}";

var parsed = engine.ParseWithoutTransformation(forLoopSource);
var forStatement = parsed.Rest.Head as Cons;

if (forStatement?.SourceReference != null)
{
    var sourceRef = forStatement.SourceReference;
    Console.WriteLine($"For loop location: {sourceRef}");
    Console.WriteLine($"Captured source text:");
    Console.WriteLine(sourceRef.GetText());
}
Console.WriteLine();

// Example 2: Transformation Tracking
Console.WriteLine("Example 2: Transformation Tracking");
Console.WriteLine("-----------------------------------");
var asyncSource = @"
async function fetchData() {
    let data = await fetch('api/data');
    return data;
}";

var (original, transformed) = engine.ParseWithTransformationSteps(asyncSource);

Console.WriteLine("Original S-Expression:");
Console.WriteLine(original.ToString().Substring(0, Math.Min(150, original.ToString().Length)) + "...");
Console.WriteLine();

Console.WriteLine("Transformed S-Expression:");
Console.WriteLine(transformed.ToString().Substring(0, Math.Min(150, transformed.ToString().Length)) + "...");
Console.WriteLine();

// Check the transformation chain
var originalFunc = original.Rest.Head as Cons;
var transformedFunc = transformed.Rest.Head as Cons;

Console.WriteLine("Transformation Chain:");
Console.WriteLine($"- Original function Origin: {(originalFunc?.Origin == null ? "null (not transformed)" : "has origin")}");
Console.WriteLine($"- Transformed function Origin: {(transformedFunc?.Origin == null ? "null (not transformed)" : "points back to original")}");

if (transformedFunc?.Origin != null)
{
    Console.WriteLine($"- Verified: Transformed points to original: {transformedFunc.Origin == originalFunc}");
    
    if (transformedFunc.Origin.SourceReference != null)
    {
        Console.WriteLine($"- Can trace back to source via origin: {transformedFunc.Origin.SourceReference}");
        Console.WriteLine($"- Original source text snippet: {transformedFunc.Origin.SourceReference.GetText().Substring(0, Math.Min(50, transformedFunc.Origin.SourceReference.GetText().Length))}...");
    }
}
Console.WriteLine();

// Example 3: Multiple levels of references
Console.WriteLine("Example 3: Tracing Through Multiple Transformations");
Console.WriteLine("---------------------------------------------------");
Console.WriteLine("If there were multiple levels of transformation, you could trace back:");
Console.WriteLine("  transformedNode.Origin.Origin.Origin...");
Console.WriteLine("until you reach a node with Origin == null (the original)");
Console.WriteLine();

Console.WriteLine("=== Demo Complete ===");

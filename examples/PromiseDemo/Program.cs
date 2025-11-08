using Asynkron.JsEngine;

Console.WriteLine("=== setTimeout and Promise Demo ===\n");

var engine = new JsEngine();

// Add a simple console.log function
engine.SetGlobalFunction("log", args =>
{
    var parts = new List<string>();
    foreach (var arg in args)
    {
        parts.Add(arg?.ToString() ?? "null");
    }
    Console.WriteLine($"   {string.Join(" ", parts)}");
    return null;
});

// Example 1: Basic setTimeout
Console.WriteLine("1. Basic setTimeout:");
await engine.Run(@"
    let message = ""Hello from setTimeout!"";
    setTimeout(function() {
        log(message);
    }, 50);
");
Console.WriteLine();

// Example 2: Promise creation and resolution
Console.WriteLine("2. Promise creation and resolution:");
await engine.Run(@"
    let p = new Promise(function(resolve, reject) {
        resolve(""Promise resolved!"");
    });
    
    p.then(function(value) {
        log(value);
    });
");
Console.WriteLine();

// Example 3: Promise chaining
Console.WriteLine("3. Promise chaining:");
await engine.Run(@"
    Promise.resolve(10)
        .then(function(x) {
            log(""Step 1:"", x);
            return x * 2;
        })
        .then(function(x) {
            log(""Step 2:"", x);
            return x + 5;
        })
        .then(function(x) {
            log(""Step 3:"", x);
        });
");
Console.WriteLine();

// Example 4: Promise with setTimeout
Console.WriteLine("4. Promise with setTimeout:");
await engine.Run(@"
    let delayedPromise = new Promise(function(resolve, reject) {
        setTimeout(function() {
            resolve(""Async value after delay"");
        }, 100);
    });
    
    delayedPromise.then(function(value) {
        log(value);
    });
");
Console.WriteLine();

// Example 5: Promise.all
Console.WriteLine("5. Promise.all:");
await engine.Run(@"
    let p1 = Promise.resolve(1);
    let p2 = Promise.resolve(2);
    let p3 = Promise.resolve(3);
    
    Promise.all([p1, p2, p3]).then(function(values) {
        log(""All resolved:"", values[0], values[1], values[2]);
    });
");
Console.WriteLine();

// Example 6: Error handling with catch (using bracket notation)
Console.WriteLine("6. Error handling with catch:");
await engine.Run(@"
    let failingPromise = Promise.reject(""Something went wrong"");
    
    failingPromise[""catch""](function(error) {
        log(""Caught error:"", error);
    });
");
Console.WriteLine();

// Example 7: Promise.race
Console.WriteLine("7. Promise.race:");
await engine.Run(@"
    let fast = Promise.resolve(""Fast promise"");
    let slow = new Promise(function(resolve) {
        setTimeout(function() {
            resolve(""Slow promise"");
        }, 100);
    });
    
    Promise.race([fast, slow]).then(function(value) {
        log(""Winner:"", value);
    });
");
Console.WriteLine();

// Example 8: setInterval with clearInterval
Console.WriteLine("8. setInterval with clearInterval:");
var counter = 0;
engine.SetGlobalFunction("incrementCounter", args =>
{
    counter++;
    Console.WriteLine($"   Interval tick {counter}");
    return null;
});

await engine.Run(@"
    let count = 0;
    let intervalId = setInterval(function() {
        incrementCounter();
        count = count + 1;
    }, 50);
    
    setTimeout(function() {
        clearInterval(intervalId);
        log(""Interval cleared after"", count, ""ticks"");
    }, 200);
");
Console.WriteLine();

Console.WriteLine("=== Demo Complete ===");

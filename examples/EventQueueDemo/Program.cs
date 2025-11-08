using Asynkron.JsEngine;

Console.WriteLine("=== Event Queue Demo ===\n");

var engine = new JsEngine();

// Example 1: Basic Run usage
Console.WriteLine("1. Basic Run usage:");
var result = await engine.Run("10 + 20;");
Console.WriteLine($"   Result: {result}\n");

// Example 2: Scheduling tasks
Console.WriteLine("2. Scheduling tasks:");
var counter = 0;

engine.ScheduleTask(() =>
{
    counter++;
    Console.WriteLine($"   Task 1 executed (counter: {counter})");
    return Task.CompletedTask;
});

engine.ScheduleTask(() =>
{
    counter++;
    Console.WriteLine($"   Task 2 executed (counter: {counter})");
    return Task.CompletedTask;
});

engine.ScheduleTask(() =>
{
    counter++;
    Console.WriteLine($"   Task 3 executed (counter: {counter})");
    return Task.CompletedTask;
});

await engine.Run("let x = 42;");
Console.WriteLine($"   Final counter value: {counter}\n");

// Example 3: Tasks scheduling more tasks
Console.WriteLine("3. Tasks scheduling more tasks:");
var executionOrder = new List<string>();

engine.ScheduleTask(() =>
{
    executionOrder.Add("Task A");
    Console.WriteLine("   Task A executed");
    
    // Schedule another task from within this task
    engine.ScheduleTask(() =>
    {
        executionOrder.Add("Task B (scheduled by A)");
        Console.WriteLine("   Task B executed (scheduled by Task A)");
        return Task.CompletedTask;
    });
    
    return Task.CompletedTask;
});

await engine.Run("let y = 100;");
Console.WriteLine($"   Execution order: {string.Join(" -> ", executionOrder)}\n");

// Example 4: Async tasks with delays
Console.WriteLine("4. Async tasks with delays:");
var asyncCounter = 0;

engine.ScheduleTask(async () =>
{
    Console.WriteLine("   Starting async task 1...");
    await Task.Delay(50);
    asyncCounter++;
    Console.WriteLine($"   Async task 1 completed (counter: {asyncCounter})");
});

engine.ScheduleTask(async () =>
{
    Console.WriteLine("   Starting async task 2...");
    await Task.Delay(100);
    asyncCounter++;
    Console.WriteLine($"   Async task 2 completed (counter: {asyncCounter})");
});

await engine.Run("let z = 200;");
Console.WriteLine($"   All async tasks completed. Final counter: {asyncCounter}\n");

// Example 5: Integration with host functions
Console.WriteLine("5. Integration with host functions:");
var messages = new List<string>();

engine.SetGlobalFunction("scheduleWork", args =>
{
    var message = args[0]?.ToString() ?? "no message";
    engine.ScheduleTask(() =>
    {
        messages.Add(message);
        Console.WriteLine($"   Scheduled work executed: {message}");
        return Task.CompletedTask;
    });
    return null;
});

await engine.Run(@"
    scheduleWork(""First"");
    scheduleWork(""Second"");
    scheduleWork(""Third"");
    ""Done scheduling"";
");

Console.WriteLine($"   Total messages processed: {messages.Count}\n");

Console.WriteLine("=== Demo Complete ===");

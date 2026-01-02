using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class LoopDebugTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task Debug_SimpleLoop()
    {
        await using var engine = CreateEngine();

        // Test 1: Simple for loop without body
        var result1 = await engine.Evaluate("var i = 0; for (; i < 3; i++) { } i;");
        Output.WriteLine($"Test1 result: {result1}");
        Assert.Equal(3d, result1);
    }

    [Fact]
    public async Task Debug_LoopWithAssignment()
    {
        await using var engine = CreateEngine();

        // Test 2: For loop with assignment
        var result2 = await engine.Evaluate("var sum = 0; for (var i = 0; i < 3; i++) { sum = sum + i; } sum;");
        Output.WriteLine($"Test2 result: {result2}");
        Assert.Equal(3d, result2); // 0+1+2=3
    }

    [Fact]
    public async Task Debug_LoopWithCompoundAssignment()
    {
        await using var engine = CreateEngine();

        // Test 3: For loop with compound assignment
        var result3 = await engine.Evaluate("var sum = 0; for (var i = 0; i < 3; i++) { sum += i; } sum;");
        Output.WriteLine($"Test3 result: {result3}");
        Assert.Equal(3d, result3); // 0+1+2=3
    }

    [Fact]
    public async Task Debug_LoopIterationCount()
    {
        await using var engine = CreateEngine();

        // Test 4: Count iterations
        var result4 = await engine.Evaluate("var count = 0; for (var i = 0; i < 3; i++) { count++; } count;");
        Output.WriteLine($"Test4 result: {result4}");
        Assert.Equal(3d, result4);
    }

    [Fact]
    public async Task Debug_SimpleAssignment()
    {
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true });

        // Test 5: Simple assignment without loop
        var result5 = await engine.Evaluate("var x = 0; x = 5; x;");
        Output.WriteLine($"Test5 result: {result5}");
        Assert.Equal(5d, result5);
    }

    [Fact]
    public async Task Debug_SimpleLetAssignment()
    {
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true });

        // Test 5: Simple assignment without loop
        var result5 = await engine.Evaluate("let x = 0; x = 5; x;");
        Output.WriteLine($"Test5 result: {result5}");
        Assert.Equal(5d, result5);
    }

    [Fact]
    public async Task Debug_SimpleAssignmentWithAdd()
    {
        await using var engine = CreateEngine();

        // Test 6: Assignment with addition without loop
        var result6 = await engine.Evaluate("var x = 0; x = x + 1; x;");
        Output.WriteLine($"Test6 result: {result6}");
        Assert.Equal(1d, result6);
    }

    [Fact]
    public async Task Debug_CompoundAssignmentNoLoop()
    {
        await using var engine = CreateEngine();

        // Test 7: Compound assignment without loop
        var result7 = await engine.Evaluate("var x = 0; x += 1; x;");
        Output.WriteLine($"Test7 result: {result7}");
        Assert.Equal(1d, result7);
    }

    [Fact]
    public async Task Debug_IncrementVsAssignment()
    {
        await using var engine = CreateEngine();

        // Test 8: Compare increment (works) vs assignment (fails)
        // Using different variable names to ensure no caching interference
        var result8a = await engine.Evaluate("var a = 0; a++; a;");
        Output.WriteLine($"Test8a (increment): {result8a}");
        Assert.Equal(1d, result8a);

        var result8b = await engine.Evaluate("var b = 0; b = 1; b;");
        Output.WriteLine($"Test8b (assignment): {result8b}");
        Assert.Equal(1d, result8b);
    }

    [Fact]
    public async Task Debug_LetAssignment()
    {
        await using var engine = CreateEngine();

        // Test 9: Let instead of var
        var result9 = await engine.Evaluate("let x = 0; x = 5; x;");
        Output.WriteLine($"Test9 (let): {result9}");
        Assert.Equal(5d, result9);
    }
}

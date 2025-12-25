using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class QuickDebugTest(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task Test_Diagnostic_SlotInfo()
    {
        var fakeLogger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = fakeLogger });
        var result = await engine.Evaluate(@"
function test(n) {
    let x = 0;
    for (let i = 0; i < n; i++) {
        x = x + 1;
    }
    return x;
}
test(3);
");
        // Print all log messages for debugging
        var records = fakeLogger.Collector.Snapshot();
        foreach (var record in records)
        {
            output.WriteLine($"[{record.Level}] {record.Message}");
        }

        // Check specifically for slot reads of 'n'
        var messages = records.Select(r => r.Message).ToList();
        var nReads = messages.Where(m => m.Contains("name=n")).ToList();
        output.WriteLine($"--- Reads of 'n': {nReads.Count} ---");
        foreach (var msg in nReads)
        {
            output.WriteLine(msg);
        }

        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_1_SimpleFunction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
function add(a, b) {
    return a + b;
}
add(1, 2);
");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2_FunctionWithVarLoop()
    {
        // Same as Test_2 but with var instead of let in the loop
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
function sum(n) {
    var total = 0;
    for (var i = 0; i < n; i++) {
        total = total + i;
    }
    return total;
}
sum(5);
");
        Assert.Equal(10d, result); // 0+1+2+3+4 = 10
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2b_FunctionWithLetLoop()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
function sum(n) {
    let total = 0;
    for (let i = 0; i < n; i++) {
        total = total + i;
    }
    return total;
}
sum(5);
");
        Assert.Equal(10d, result); // 0+1+2+3+4 = 10
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2c_SimplerLetLoop()
    {
        await using var engine = new JsEngine();
        // Simpler: increment inside loop
        var result = await engine.Evaluate(@"
function test() {
    let x = 0;
    for (let i = 0; i < 3; i++) {
        x = x + 1;
    }
    return x;
}
test();
");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2d_NoLetInLoop()
    {
        await using var engine = new JsEngine();
        // No let in the loop at all
        var result = await engine.Evaluate(@"
function test() {
    let x = 0;
    let i = 0;
    for (; i < 3; i++) {
        x = x + 1;
    }
    return x;
}
test();
");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2e_LetLoopWithLoopVar()
    {
        await using var engine = new JsEngine();
        // Uses loop variable i in the body
        var result = await engine.Evaluate(@"
function test() {
    let x = 0;
    for (let i = 0; i < 3; i++) {
        x = x + i;  // Uses i from the loop
    }
    return x;
}
test();
");
        Assert.Equal(3d, result); // 0+1+2 = 3
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2f_LetLoopJustReadI()
    {
        await using var engine = new JsEngine();
        // Just reads i
        var result = await engine.Evaluate(@"
function test() {
    let last = 0;
    for (let i = 0; i < 3; i++) {
        last = i;
    }
    return last;
}
test();
");
        Assert.Equal(2d, result); // last value of i is 2
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2g_WithParameter()
    {
        await using var engine = new JsEngine();
        // Uses parameter in loop condition
        var result = await engine.Evaluate(@"
function test(n) {
    let x = 0;
    for (let i = 0; i < n; i++) {
        x = x + 1;
    }
    return x;
}
test(3);
");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2h_WithParameterAndLoopVar()
    {
        await using var engine = new JsEngine();
        // Uses parameter AND loop variable
        var result = await engine.Evaluate(@"
function test(n) {
    let x = 0;
    for (let i = 0; i < n; i++) {
        x = x + i;
    }
    return x;
}
test(3);
");
        Assert.Equal(3d, result); // 0+1+2 = 3
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2i_CanReadParameter()
    {
        await using var engine = new JsEngine();
        // Just check if parameter is readable
        var result = await engine.Evaluate(@"
function test(n) {
    return n;
}
test(42);
");
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2j_ParameterInCondition()
    {
        await using var engine = new JsEngine();
        // Check if parameter is readable inside loop condition
        var result = await engine.Evaluate(@"
function test(n) {
    for (let i = 0; i < n; i++) {
        return 'entered loop';
    }
    return 'never entered loop, n=' + n;
}
test(3);
");
        Assert.Equal("entered loop", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_2k_TopLevelForLet()
    {
        // Top-level for loop (no function) - uses different code path
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let x = 0;
for (let i = 0; i < 3; i++) {
    x = x + 1;
}
x;
");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Test_3_FunctionWithString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
function getLength(str) {
    return str.length;
}
getLength('hello');
");
        Assert.Equal(5d, result);
    }
}

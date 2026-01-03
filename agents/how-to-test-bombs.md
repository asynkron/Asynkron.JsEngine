# Test Bomb Methodology

Use when root cause is unclear; each test targets one hypothesis.

## Steps
1. List suspected causes.
2. Write one test per hypothesis (`H1_...`, `H2_...`), each with a clear expected outcome and doc comment.
3. Run all together; the pass/fail pattern reveals the failing area.
4. Add edge-case hypotheses as you learn more.

## Template
```csharp
/// TEST BOMB: Systematic elimination of suspected causes for [BUG].
public class MyBugTestBomb
{
    private readonly ITestOutputHelper _output;
    public MyBugTestBomb(ITestOutputHelper output) => _output = output;

    /// H1: [description]
    [Fact(Timeout = 10000)]
    public async Task H1_FirstHypothesis()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("/* test code */");
        _output.WriteLine($"H1 Result: {result}");
        Assert.Equal("expected", result?.ToString());
    }

    /// H2: [description]
    [Fact(Timeout = 10000)]
    public async Task H2_SecondHypothesis()
    {
        // ...
    }
}
```

## Benefits
- Systematic and documented
- Fast to run in parallel
- Reusable regression coverage
- Can prove an area is not the bug source

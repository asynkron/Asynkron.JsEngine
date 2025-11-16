namespace Asynkron.JsEngine.Tests;

public class LogicalAssignmentOperatorsTests
{
    [Fact(Timeout = 2000)]
    public async Task LogicalAndAssignment_AssignsWhenTruthy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 5;
                                                       x &&= 10;
                                                       x;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalAndAssignment_DoesNotAssignWhenFalsy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 0;
                                                       x &&= 10;
                                                       x;

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalOrAssignment_AssignsWhenFalsy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 0;
                                                       x ||= 10;
                                                       x;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalOrAssignment_DoesNotAssignWhenTruthy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 5;
                                                       x ||= 10;
                                                       x;

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullishCoalescingAssignment_AssignsWhenNullOrUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = null;
                                                       x ??= 10;
                                                       x;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullishCoalescingAssignment_DoesNotAssignWhenNotNullish()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 0;
                                                       x ??= 10;
                                                       x;

                                           """);
        Assert.Equal(0d, result); // 0 is not nullish, so not replaced
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalAssignment_WorksWithObjects()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 5, b: 0 };
                                                       obj.a &&= 20;
                                                       obj.b ||= 30;
                                                       obj.a + obj.b;

                                           """);
        Assert.Equal(20d + 30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullishCoalescingAssignment_WithUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = undefined;
                                                       x ??= 42;
                                                       x;

                                           """);
        Assert.Equal(42d, result);
    }
}

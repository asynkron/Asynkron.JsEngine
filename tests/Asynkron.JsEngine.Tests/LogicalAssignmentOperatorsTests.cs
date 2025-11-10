using Xunit;

namespace Asynkron.JsEngine.Tests;

public class LogicalAssignmentOperatorsTests
{
    [Fact]
    public async Task LogicalAndAssignment_AssignsWhenTruthy()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 5;
            x &&= 10;
            x;
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task LogicalAndAssignment_DoesNotAssignWhenFalsy()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 0;
            x &&= 10;
            x;
        ");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task LogicalOrAssignment_AssignsWhenFalsy()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 0;
            x ||= 10;
            x;
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task LogicalOrAssignment_DoesNotAssignWhenTruthy()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 5;
            x ||= 10;
            x;
        ");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task NullishCoalescingAssignment_AssignsWhenNullOrUndefined()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = null;
            x ??= 10;
            x;
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task NullishCoalescingAssignment_DoesNotAssignWhenNotNullish()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 0;
            x ??= 10;
            x;
        ");
        Assert.Equal(0d, result); // 0 is not nullish, so not replaced
    }

    [Fact]
    public async Task LogicalAssignment_WorksWithObjects()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 5, b: 0 };
            obj.a &&= 20;
            obj.b ||= 30;
            obj.a + obj.b;
        ");
        Assert.Equal(20d + 30d, result);
    }

    [Fact]
    public async Task NullishCoalescingAssignment_WithUndefined()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = undefined;
            x ??= 42;
            x;
        ");
        Assert.Equal(42d, result);
    }
}

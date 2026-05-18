using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.TypeSystem)]
public sealed class LogicalAssignmentOperatorsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task LogicalAndAssignment_AssignsWhenTruthy()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let x = undefined;
                                                       x ??= 42;
                                                       x;

                                           """);
        Assert.Equal(42d, result);
    }

    [Theory(Timeout = 2000)]
    [InlineData("&&=", "1")]
    [InlineData("||=", "0")]
    [InlineData("??=", "null")]
    public async Task LogicalAssignment_AppliesNameInferenceForIdentifierAssignments(string op, string initialValue)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""

                                                       let fn = {{initialValue}};
                                                       fn {{op}} function() {};
                                                       let arrow = {{initialValue}};
                                                       arrow {{op}} () => 1;
                                                       let cls = {{initialValue}};
                                                       cls {{op}} class {};
                                                       fn.name + "," + arrow.name + "," + cls.name;

                                           """);
        Assert.Equal("fn,arrow,cls", result);
    }

    [Theory(Timeout = 2000)]
    [InlineData("&&=", "1")]
    [InlineData("||=", "0")]
    [InlineData("??=", "null")]
    public async Task LogicalAssignment_DoesNotApplyNameInferenceForMemberAssignments(string op, string initialValue)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""

                                                       let obj = { fn: {{initialValue}} };
                                                       obj.fn {{op}} function() {};
                                                       obj.fn.name;

                                           """);
        Assert.Equal(string.Empty, result);
    }

    [Theory(Timeout = 2000)]
    [InlineData("&&=", "1")]
    [InlineData("||=", "0")]
    [InlineData("??=", "null")]
    public async Task LogicalAssignment_StrictGetterOnlyMemberThrowsWhenAssignmentBranchRuns(
        string op,
        string initialValue)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""

                                                       "use strict";
                                                       let obj = {};
                                                       Object.defineProperty(obj, "value", {
                                                           get: function() { return {{initialValue}}; }
                                                       });
                                                       try {
                                                           obj.value {{op}} 2;
                                                           "no throw";
                                                       } catch (error) {
                                                           error.name;
                                                       }

                                           """);
        Assert.Equal("TypeError", result);
    }

    [Theory(Timeout = 2000)]
    [InlineData("&&=", "1")]
    [InlineData("||=", "0")]
    [InlineData("??=", "null")]
    public async Task LogicalAssignment_StrictNonWritableMemberThrowsWhenAssignmentBranchRuns(
        string op,
        string initialValue)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""

                                                       "use strict";
                                                       let obj = {};
                                                       Object.defineProperty(obj, "value", {
                                                           value: {{initialValue}},
                                                           writable: false
                                                       });
                                                       try {
                                                           obj.value {{op}} 2;
                                                           "no throw";
                                                       } catch (error) {
                                                           error.name;
                                                       }

                                           """);
        Assert.Equal("TypeError", result);
    }

    [Theory(Timeout = 2000)]
    [InlineData("&&=", "0")]
    [InlineData("||=", "1")]
    [InlineData("??=", "1")]
    public async Task LogicalAssignment_SkipsStrictMemberWriteWhenShortCircuiting(string op, string initialValue)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""

                                                       "use strict";
                                                       let obj = {};
                                                       Object.defineProperty(obj, "value", {
                                                           value: {{initialValue}},
                                                           writable: false
                                                       });
                                                       try {
                                                           let result = (obj.value {{op}} 2);
                                                           result;
                                                       } catch (error) {
                                                           error.name;
                                                       }

                                           """);
        Assert.Equal(double.Parse(initialValue, System.Globalization.CultureInfo.InvariantCulture), result);
    }
}

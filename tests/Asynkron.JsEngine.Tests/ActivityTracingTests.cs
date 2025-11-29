using System.Diagnostics;
using Asynkron.JsEngine.Tests.Tracing;

namespace Asynkron.JsEngine.Tests;

public class ActivityTracingTests
{
    [Fact]
    public async Task EvaluatorActivitiesAttachToTestRoot()
    {
        await using var engine = new JsEngine();
        using var root = new Activity("JsEngine.TraceTest");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var result = await engine.Evaluate("""

                                                   let total = 0;
                                                   function increment(value) {
                                                       return value + 1;
                                                   }

                                                   total = increment(total);
                                                   total = increment(total);
                                                   total;

                                       """);

        Assert.Equal(2d, result);

        var activities = recorder.Activities;
        Assert.True(activities.Count > 0, "Expected evaluator activities to be recorded");
        Assert.Contains(activities, activity => activity.DisplayName == "Program");
        Assert.Contains(activities, activity => activity.DisplayName.StartsWith("Statement:", StringComparison.Ordinal));
        Assert.All(activities,
            activity => Assert.Equal(recorder.RootActivity.TraceId, activity.TraceId));

        root.Stop();
    }
}

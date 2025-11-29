using System.Diagnostics;
using System.Linq;
using Asynkron.JsEngine.Tests.Tracing;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ActivityTracingTests
{
    private readonly ITestOutputHelper _output;

    public ActivityTracingTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
        ActivityTimelineFormatter.Write(recorder.RootActivity,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    [Fact]
    public async Task EventQueueTasksInheritActivityContext()
    {
        await using var engine = new JsEngine();
        using var root = new Activity("JsEngine.AsyncTrace");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        await engine.Evaluate("""

                                           setTimeout(() => {
                                               {
                                                   function asyncHoisted() { return 1; }
                                                   asyncHoisted();
                                               }
                                           }, 0);

                               """);

        root.Stop();

        var traceIds = recorder.Activities.Select(activity => activity.TraceId).Distinct().ToArray();
        Assert.Single(traceIds);
        Assert.Equal(root.TraceId, traceIds[0]);
        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Scope:Block");

        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }
}

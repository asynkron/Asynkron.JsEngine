using System;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Tracing;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Asynkron.JsEngine.Tests.Test262;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
internal sealed class Test262ActivityTraceAttribute : Attribute, ITestAction
{
    private static readonly ConditionalWeakTable<ITest, RecorderHandle> RecorderHandles = new();

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        if (test is null || test.IsSuite)
        {
            return;
        }

        var recorder = EvaluatorActivityRecorder.StartNew(FormattableString.Invariant($"Test262:{test.FullName}"));
        RecorderHandles.Add(test, new RecorderHandle(recorder));
    }

    public void AfterTest(ITest test)
    {
        if (test is null || test.IsSuite)
        {
            return;
        }

        if (!RecorderHandles.TryGetValue(test, out var handle))
        {
            return;
        }

        RecorderHandles.Remove(test);

        using (handle)
        {
            var result = TestExecutionContext.CurrentContext.CurrentResult;
            var shouldLogTimeline = result.ResultState.Status == TestStatus.Failed;

            if (!shouldLogTimeline)
            {
                return;
            }

            var activities = handle.Recorder.Activities;
            if (activities.Count == 0)
            {
                TestContext.Progress.WriteLine(FormattableString.Invariant(
                    $"Activity timeline for {test.FullName}: (no evaluator activities recorded)"));
                return;
            }

            var timeline = ActivityTimelineFormatter.Write(handle.Recorder.RootActivity,
                activities,
                predicate: activity => !string.Equals(activity.DisplayName, test.FullName, StringComparison.Ordinal));

            if (!string.IsNullOrWhiteSpace(timeline))
            {
                TestContext.Progress.WriteLine(FormattableString.Invariant(
                    $"Activity timeline for {test.FullName}:"));
                TestContext.Progress.WriteLine(timeline);
            }
        }
    }

    private sealed class RecorderHandle(EvaluatorActivityRecorder recorder) : IDisposable
    {
        public EvaluatorActivityRecorder Recorder { get; } = recorder;

        public void Dispose()
        {
            Recorder.Dispose();
        }
    }
}

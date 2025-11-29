using System;
using System.Collections.Generic;
using System.Diagnostics;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests.Tracing;

/// <summary>
///     Captures the evaluator's ActivitySource output for test assertions.
///     Creates (or attaches to) a parent <see cref="Activity" /> so evaluator spans
///     become children of a known root.
/// </summary>
public sealed class EvaluatorActivityRecorder : IDisposable
{
    private readonly List<Activity> _activities = new();
    private readonly object _gate = new();
    private readonly ActivityListener _listener;
    private readonly bool _ownsRoot;

    private EvaluatorActivityRecorder(Activity rootActivity, bool ownsRoot)
    {
        RootActivity = rootActivity ?? throw new ArgumentNullException(nameof(rootActivity));
        _ownsRoot = ownsRoot;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, TypedAstEvaluator.ActivitySourceName,
                StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate)
                {
                    _activities.Add(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    ///     Starts a new root <see cref="Activity" /> (named <paramref name="rootName" />)
    ///     and captures all evaluator spans that occur beneath it.
    /// </summary>
    public static EvaluatorActivityRecorder StartNew(string rootName = "EvaluatorTestScope")
    {
        var root = new Activity(rootName);
        root.Start();
        return new EvaluatorActivityRecorder(root, ownsRoot: true);
    }

    /// <summary>
    ///     Attaches to an already-started root <see cref="Activity" />. The caller owns
    ///     the lifecycle of <paramref name="rootActivity" />.
    /// </summary>
    public static EvaluatorActivityRecorder Attach(Activity rootActivity)
    {
        ArgumentNullException.ThrowIfNull(rootActivity);

        if (rootActivity.Id is null)
        {
            rootActivity.Start();
        }

        return new EvaluatorActivityRecorder(rootActivity, ownsRoot: false);
    }

    /// <summary>
    ///     The root activity that evaluator spans will attach to.
    /// </summary>
    public Activity RootActivity { get; }

    /// <summary>
    ///     Snapshot of all evaluator activities observed (ordered by stop time).
    /// </summary>
    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (_gate)
            {
                return _activities.ToArray();
            }
        }
    }

    public void Dispose()
    {
        if (_ownsRoot)
        {
            RootActivity.Stop();
        }

        _listener.Dispose();
    }
}

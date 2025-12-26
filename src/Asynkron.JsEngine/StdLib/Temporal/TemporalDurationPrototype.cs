#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.Duration represents a duration of time
/// </summary>
[JsPrototype("Duration", ToStringTag = "Temporal.Duration")]
public sealed partial class TemporalDurationPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Duration.prototype.add
        throw new NotImplementedException("Temporal.Duration.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Duration.prototype.subtract
        throw new NotImplementedException("Temporal.Duration.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("negated", Length = 0d)]
    public JsValue Negated(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.negated
        throw new NotImplementedException("Temporal.Duration.prototype.negated is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("abs", Length = 0d)]
    public JsValue Abs(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.abs
        throw new NotImplementedException("Temporal.Duration.prototype.abs is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("round", Length = 1d)]
    public JsValue Round(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Duration.prototype.round
        throw new NotImplementedException("Temporal.Duration.prototype.round is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("total", Length = 1d)]
    public JsValue Total(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Duration.prototype.total
        throw new NotImplementedException("Temporal.Duration.prototype.total is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Duration.prototype.toString
        throw new NotImplementedException("Temporal.Duration.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("years")]
    public JsValue Years(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.years
        throw new NotImplementedException("Temporal.Duration.prototype.years is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("months")]
    public JsValue Months(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.months
        throw new NotImplementedException("Temporal.Duration.prototype.months is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("days")]
    public JsValue Days(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.days
        throw new NotImplementedException("Temporal.Duration.prototype.days is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("hours")]
    public JsValue Hours(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.hours
        throw new NotImplementedException("Temporal.Duration.prototype.hours is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("minutes")]
    public JsValue Minutes(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.minutes
        throw new NotImplementedException("Temporal.Duration.prototype.minutes is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("seconds")]
    public JsValue Seconds(JsValue thisValue)
    {
        // TODO: Implement Temporal.Duration.prototype.seconds
        throw new NotImplementedException("Temporal.Duration.prototype.seconds is not yet implemented");
    }
}

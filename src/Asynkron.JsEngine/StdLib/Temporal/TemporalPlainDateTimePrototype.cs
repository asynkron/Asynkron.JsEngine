#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.PlainDateTime represents a calendar date and wall-clock time
/// </summary>
[JsPrototype("PlainDateTime", ToStringTag = "Temporal.PlainDateTime")]
public sealed partial class TemporalPlainDateTimePrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.add
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.subtract
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("with", Length = 1d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.with
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.with is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("until", Length = 1d)]
    public JsValue Until(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.until
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.until is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("since", Length = 1d)]
    public JsValue Since(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.since
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.since is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("round", Length = 1d)]
    public JsValue Round(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.round
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.round is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.equals
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.toString
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("year")]
    public JsValue Year(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.year
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.year is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("month")]
    public JsValue Month(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.month
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.month is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("day")]
    public JsValue Day(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.day
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.day is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("hour")]
    public JsValue Hour(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.hour
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.hour is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("minute")]
    public JsValue Minute(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.minute
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.minute is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("second")]
    public JsValue Second(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDateTime.prototype.second
        throw new NotImplementedException("Temporal.PlainDateTime.prototype.second is not yet implemented");
    }
}

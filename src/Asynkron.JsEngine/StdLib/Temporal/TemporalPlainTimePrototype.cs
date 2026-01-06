#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.PlainTime represents a wall-clock time (hour, minute, second, etc.)
/// </summary>
[JsPrototype("PlainTime", ToStringTag = "Temporal.PlainTime")]
public sealed partial class TemporalPlainTimePrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.add
        throw new NotImplementedException("Temporal.PlainTime.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.subtract
        throw new NotImplementedException("Temporal.PlainTime.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("with", Length = 1d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.with
        throw new NotImplementedException("Temporal.PlainTime.prototype.with is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("until", Length = 1d)]
    public JsValue Until(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.until
        throw new NotImplementedException("Temporal.PlainTime.prototype.until is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("since", Length = 1d)]
    public JsValue Since(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.since
        throw new NotImplementedException("Temporal.PlainTime.prototype.since is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("round", Length = 1d)]
    public JsValue Round(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.round
        throw new NotImplementedException("Temporal.PlainTime.prototype.round is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.equals
        throw new NotImplementedException("Temporal.PlainTime.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainTime.prototype.toString
        throw new NotImplementedException("Temporal.PlainTime.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("hour")]
    public JsValue Hour(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainTime.prototype.hour
        throw new NotImplementedException("Temporal.PlainTime.prototype.hour is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("minute")]
    public JsValue Minute(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainTime.prototype.minute
        throw new NotImplementedException("Temporal.PlainTime.prototype.minute is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("second")]
    public JsValue Second(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainTime.prototype.second
        throw new NotImplementedException("Temporal.PlainTime.prototype.second is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("millisecond")]
    public JsValue Millisecond(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainTime.prototype.millisecond
        throw new NotImplementedException("Temporal.PlainTime.prototype.millisecond is not yet implemented");
    }
}

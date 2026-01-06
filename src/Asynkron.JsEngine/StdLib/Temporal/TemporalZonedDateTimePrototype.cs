#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.ZonedDateTime represents a date and time in a specific time zone
/// </summary>
[JsPrototype("ZonedDateTime", ToStringTag = "Temporal.ZonedDateTime")]
public sealed partial class TemporalZonedDateTimePrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.add
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.subtract
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("with", Length = 1d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.with
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.with is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("withPlainTime", Length = 1d)]
    public JsValue WithPlainTime(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.withPlainTime
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.withPlainTime is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("withTimeZone", Length = 1d)]
    public JsValue WithTimeZone(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.withTimeZone
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.withTimeZone is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("until", Length = 1d)]
    public JsValue Until(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.until
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.until is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("since", Length = 1d)]
    public JsValue Since(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.since
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.since is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("round", Length = 1d)]
    public JsValue Round(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.round
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.round is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.equals
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.toString
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("year")]
    public JsValue Year(JsValue thisValue)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.year
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.year is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("month")]
    public JsValue Month(JsValue thisValue)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.month
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.month is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("day")]
    public JsValue Day(JsValue thisValue)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.day
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.day is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("hour")]
    public JsValue Hour(JsValue thisValue)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.hour
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.hour is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("minute")]
    public JsValue Minute(JsValue thisValue)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.minute
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.minute is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("timeZone")]
    public JsValue TimeZone(JsValue thisValue)
    {
        // TODO: Implement Temporal.ZonedDateTime.prototype.timeZone
        throw new NotImplementedException("Temporal.ZonedDateTime.prototype.timeZone is not yet implemented");
    }
}

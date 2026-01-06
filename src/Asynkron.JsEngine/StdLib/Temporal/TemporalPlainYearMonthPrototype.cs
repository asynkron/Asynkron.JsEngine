#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.PlainYearMonth represents a year and month in a calendar
/// </summary>
[JsPrototype("PlainYearMonth", ToStringTag = "Temporal.PlainYearMonth")]
public sealed partial class TemporalPlainYearMonthPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.add
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.subtract
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("with", Length = 1d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.with
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.with is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("until", Length = 1d)]
    public JsValue Until(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.until
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.until is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("since", Length = 1d)]
    public JsValue Since(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.since
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.since is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.equals
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.toString
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("year")]
    public JsValue Year(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.year
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.year is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("month")]
    public JsValue Month(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainYearMonth.prototype.month
        throw new NotImplementedException("Temporal.PlainYearMonth.prototype.month is not yet implemented");
    }
}

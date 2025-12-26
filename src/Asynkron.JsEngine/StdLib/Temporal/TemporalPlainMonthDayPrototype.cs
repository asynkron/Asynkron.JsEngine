#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.PlainMonthDay represents a month and day in a calendar
/// </summary>
[JsPrototype("PlainMonthDay", ToStringTag = "Temporal.PlainMonthDay")]
public sealed partial class TemporalPlainMonthDayPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("with", Length = 1d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainMonthDay.prototype.with
        throw new NotImplementedException("Temporal.PlainMonthDay.prototype.with is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainMonthDay.prototype.equals
        throw new NotImplementedException("Temporal.PlainMonthDay.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainMonthDay.prototype.toString
        throw new NotImplementedException("Temporal.PlainMonthDay.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("monthCode")]
    public JsValue MonthCode(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainMonthDay.prototype.monthCode
        throw new NotImplementedException("Temporal.PlainMonthDay.prototype.monthCode is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("day")]
    public JsValue Day(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainMonthDay.prototype.day
        throw new NotImplementedException("Temporal.PlainMonthDay.prototype.day is not yet implemented");
    }
}

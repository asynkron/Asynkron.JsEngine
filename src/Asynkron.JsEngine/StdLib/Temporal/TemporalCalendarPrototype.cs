#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.Calendar represents a calendar system
/// </summary>
[JsPrototype("Calendar", ToStringTag = "Temporal.Calendar")]
public sealed partial class TemporalCalendarPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("dateFromFields", Length = 1d)]
    public JsValue DateFromFields(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.dateFromFields
        throw new NotImplementedException("Temporal.Calendar.prototype.dateFromFields is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("yearMonthFromFields", Length = 1d)]
    public JsValue YearMonthFromFields(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.yearMonthFromFields
        throw new NotImplementedException("Temporal.Calendar.prototype.yearMonthFromFields is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("monthDayFromFields", Length = 1d)]
    public JsValue MonthDayFromFields(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.monthDayFromFields
        throw new NotImplementedException("Temporal.Calendar.prototype.monthDayFromFields is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("dateAdd", Length = 2d)]
    public JsValue DateAdd(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.dateAdd
        throw new NotImplementedException("Temporal.Calendar.prototype.dateAdd is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("dateUntil", Length = 2d)]
    public JsValue DateUntil(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.dateUntil
        throw new NotImplementedException("Temporal.Calendar.prototype.dateUntil is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("year", Length = 1d)]
    public JsValue Year(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.year
        throw new NotImplementedException("Temporal.Calendar.prototype.year is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("month", Length = 1d)]
    public JsValue Month(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.month
        throw new NotImplementedException("Temporal.Calendar.prototype.month is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("day", Length = 1d)]
    public JsValue Day(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.day
        throw new NotImplementedException("Temporal.Calendar.prototype.day is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Calendar.prototype.toString
        throw new NotImplementedException("Temporal.Calendar.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("id")]
    public JsValue Id(JsValue thisValue)
    {
        // TODO: Implement Temporal.Calendar.prototype.id
        throw new NotImplementedException("Temporal.Calendar.prototype.id is not yet implemented");
    }
}

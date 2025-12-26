#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.PlainDate represents a calendar date (year, month, day)
/// </summary>
[JsPrototype("PlainDate", ToStringTag = "Temporal.PlainDate")]
public sealed partial class TemporalPlainDatePrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.add
        throw new NotImplementedException("Temporal.PlainDate.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.subtract
        throw new NotImplementedException("Temporal.PlainDate.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("with", Length = 1d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.with
        throw new NotImplementedException("Temporal.PlainDate.prototype.with is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("until", Length = 1d)]
    public JsValue Until(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.until
        throw new NotImplementedException("Temporal.PlainDate.prototype.until is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("since", Length = 1d)]
    public JsValue Since(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.since
        throw new NotImplementedException("Temporal.PlainDate.prototype.since is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.equals
        throw new NotImplementedException("Temporal.PlainDate.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.PlainDate.prototype.toString
        throw new NotImplementedException("Temporal.PlainDate.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("year")]
    public JsValue Year(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDate.prototype.year
        throw new NotImplementedException("Temporal.PlainDate.prototype.year is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("month")]
    public JsValue Month(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDate.prototype.month
        throw new NotImplementedException("Temporal.PlainDate.prototype.month is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("day")]
    public JsValue Day(JsValue thisValue)
    {
        // TODO: Implement Temporal.PlainDate.prototype.day
        throw new NotImplementedException("Temporal.PlainDate.prototype.day is not yet implemented");
    }
}

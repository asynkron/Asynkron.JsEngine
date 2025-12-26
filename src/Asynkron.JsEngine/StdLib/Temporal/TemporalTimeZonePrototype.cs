#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.TimeZone represents a time zone
/// </summary>
[JsPrototype("TimeZone", ToStringTag = "Temporal.TimeZone")]
public sealed partial class TemporalTimeZonePrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("getOffsetNanosecondsFor", Length = 1d)]
    public JsValue GetOffsetNanosecondsFor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getOffsetNanosecondsFor
        throw new NotImplementedException("Temporal.TimeZone.prototype.getOffsetNanosecondsFor is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getOffsetStringFor", Length = 1d)]
    public JsValue GetOffsetStringFor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getOffsetStringFor
        throw new NotImplementedException("Temporal.TimeZone.prototype.getOffsetStringFor is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getPlainDateTimeFor", Length = 1d)]
    public JsValue GetPlainDateTimeFor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getPlainDateTimeFor
        throw new NotImplementedException("Temporal.TimeZone.prototype.getPlainDateTimeFor is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getInstantFor", Length = 1d)]
    public JsValue GetInstantFor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getInstantFor
        throw new NotImplementedException("Temporal.TimeZone.prototype.getInstantFor is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getPossibleInstantsFor", Length = 1d)]
    public JsValue GetPossibleInstantsFor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getPossibleInstantsFor
        throw new NotImplementedException("Temporal.TimeZone.prototype.getPossibleInstantsFor is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getNextTransition", Length = 1d)]
    public JsValue GetNextTransition(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getNextTransition
        throw new NotImplementedException("Temporal.TimeZone.prototype.getNextTransition is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("getPreviousTransition", Length = 1d)]
    public JsValue GetPreviousTransition(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.getPreviousTransition
        throw new NotImplementedException("Temporal.TimeZone.prototype.getPreviousTransition is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.TimeZone.prototype.toString
        throw new NotImplementedException("Temporal.TimeZone.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("id")]
    public JsValue Id(JsValue thisValue)
    {
        // TODO: Implement Temporal.TimeZone.prototype.id
        throw new NotImplementedException("Temporal.TimeZone.prototype.id is not yet implemented");
    }
}

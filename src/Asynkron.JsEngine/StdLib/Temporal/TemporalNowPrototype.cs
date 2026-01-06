#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.Now provides methods to get the current date/time
/// </summary>
[JsPrototype("Now", ToStringTag = "Temporal.Now")]
public sealed partial class TemporalNowPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("instant", Length = 0d)]
    public JsValue Instant(JsValue thisValue)
    {
        // TODO: Implement Temporal.Now.instant
        throw new NotImplementedException("Temporal.Now.instant is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("timeZoneId", Length = 0d)]
    public JsValue TimeZoneId(JsValue thisValue)
    {
        // TODO: Implement Temporal.Now.timeZoneId
        throw new NotImplementedException("Temporal.Now.timeZoneId is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("zonedDateTime", Length = 1d)]
    public JsValue ZonedDateTime(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Now.zonedDateTime
        throw new NotImplementedException("Temporal.Now.zonedDateTime is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("zonedDateTimeISO", Length = 0d)]
    public JsValue ZonedDateTimeISO(JsValue thisValue)
    {
        // TODO: Implement Temporal.Now.zonedDateTimeISO
        throw new NotImplementedException("Temporal.Now.zonedDateTimeISO is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("plainDateTime", Length = 1d)]
    public JsValue PlainDateTime(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Now.plainDateTime
        throw new NotImplementedException("Temporal.Now.plainDateTime is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("plainDateTimeISO", Length = 0d)]
    public JsValue PlainDateTimeISO(JsValue thisValue)
    {
        // TODO: Implement Temporal.Now.plainDateTimeISO
        throw new NotImplementedException("Temporal.Now.plainDateTimeISO is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("plainDate", Length = 1d)]
    public JsValue PlainDate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Now.plainDate
        throw new NotImplementedException("Temporal.Now.plainDate is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("plainDateISO", Length = 0d)]
    public JsValue PlainDateISO(JsValue thisValue)
    {
        // TODO: Implement Temporal.Now.plainDateISO
        throw new NotImplementedException("Temporal.Now.plainDateISO is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("plainTimeISO", Length = 0d)]
    public JsValue PlainTimeISO(JsValue thisValue)
    {
        // TODO: Implement Temporal.Now.plainTimeISO
        throw new NotImplementedException("Temporal.Now.plainTimeISO is not yet implemented");
    }
}

#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib.Temporal;

/// <summary>
/// Temporal.Instant represents a fixed point in time
/// </summary>
[JsPrototype("Instant", ToStringTag = "Temporal.Instant")]
public sealed partial class TemporalInstantPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.add
        throw new NotImplementedException("Temporal.Instant.prototype.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("subtract", Length = 1d)]
    public JsValue Subtract(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.subtract
        throw new NotImplementedException("Temporal.Instant.prototype.subtract is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("until", Length = 1d)]
    public JsValue Until(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.until
        throw new NotImplementedException("Temporal.Instant.prototype.until is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("since", Length = 1d)]
    public JsValue Since(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.since
        throw new NotImplementedException("Temporal.Instant.prototype.since is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("round", Length = 1d)]
    public JsValue Round(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.round
        throw new NotImplementedException("Temporal.Instant.prototype.round is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("equals", Length = 1d)]
    public JsValue Equals(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.equals
        throw new NotImplementedException("Temporal.Instant.prototype.equals is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Temporal.Instant.prototype.toString
        throw new NotImplementedException("Temporal.Instant.prototype.toString is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("toJSON", Length = 0d)]
    public JsValue ToJSON(JsValue thisValue)
    {
        // TODO: Implement Temporal.Instant.prototype.toJSON
        throw new NotImplementedException("Temporal.Instant.prototype.toJSON is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue)
    {
        // TODO: Implement Temporal.Instant.prototype.valueOf
        throw new NotImplementedException("Temporal.Instant.prototype.valueOf is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("epochMilliseconds")]
    public JsValue EpochMilliseconds(JsValue thisValue)
    {
        // TODO: Implement Temporal.Instant.prototype.epochMilliseconds
        throw new NotImplementedException("Temporal.Instant.prototype.epochMilliseconds is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("epochNanoseconds")]
    public JsValue EpochNanoseconds(JsValue thisValue)
    {
        // TODO: Implement Temporal.Instant.prototype.epochNanoseconds
        throw new NotImplementedException("Temporal.Instant.prototype.epochNanoseconds is not yet implemented");
    }
}

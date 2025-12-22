namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JsPrototypeAttribute(string intrinsicName) : Attribute
{
    /// <summary>
    ///     Friendly name for diagnostics (e.g. "Intl.Locale").
    /// </summary>
    public string IntrinsicName { get; } = intrinsicName;

    /// <summary>
    ///     Optional value for %prototype%[@@toStringTag]; when specified the source generator
    ///     emits the descriptor automatically.
    /// </summary>
    public string? ToStringTag { get; set; }

    /// <summary>
    ///     Specifies which underlying object implementation should back the prototype.
    ///     Defaults to a plain <see cref="JsObject" />.
    /// </summary>
    public PrototypeObjectKind ObjectKind { get; set; } = PrototypeObjectKind.Object;
}

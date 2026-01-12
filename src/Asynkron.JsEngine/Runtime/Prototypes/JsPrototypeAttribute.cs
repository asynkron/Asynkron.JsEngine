using JetBrains.Annotations;

namespace Asynkron.JsEngine.Runtime.Prototypes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JsPrototypeAttribute(string intrinsicName) : Attribute
{
    /// <summary>
    ///     Friendly name for diagnostics (e.g. "Intl.Locale").
    /// </summary>
    [UsedImplicitly]
    public string IntrinsicName { get; } = intrinsicName;

    /// <summary>
    ///     Optional value for %prototype%[@@toStringTag]; when specified the source generator
    ///     emits the descriptor automatically.
    /// </summary>
    [UsedImplicitly]
    public string? ToStringTag { get; set; }

    /// <summary>
    ///     Specifies which underlying object implementation should back the prototype.
    ///     Defaults to a plain <see cref="JsObject" />.
    /// </summary>
    [UsedImplicitly]
    public PrototypeObjectKind ObjectKind { get; set; } = PrototypeObjectKind.Object;

    /// <summary>
    ///     When specified, the source generator emits a <c>RequireInstance</c> method
    ///     that extracts and validates the concrete instance type from <c>thisValue</c>.
    /// </summary>
    [UsedImplicitly]
    public Type? InstanceType { get; set; }

    /// <summary>
    ///     When specified along with <see cref="InstanceType"/>, the generator uses this
    ///     static method (e.g. "TryGetInternalPromise") instead of <c>TryGetObject&lt;T&gt;</c>.
    ///     The method must have signature: <c>static bool MethodName(JsValue, out T?)</c>.
    /// </summary>
    [UsedImplicitly]
    public string? TryGetMethod { get; set; }
}

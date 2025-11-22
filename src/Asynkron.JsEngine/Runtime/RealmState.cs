namespace Asynkron.JsEngine.Runtime;

using JsTypes;

/// <summary>
/// Holds per-engine realm state such as intrinsic prototypes and constructors,
/// so we do not rely on mutable StandardLibrary statics across realms.
/// </summary>
public sealed class RealmState
{
    public JsObject? ObjectPrototype { get; set; }
    public JsObject? FunctionPrototype { get; set; }
    public JsObject? ArrayPrototype { get; set; }
    public JsObject? ErrorPrototype { get; set; }
    public JsObject? TypeErrorPrototype { get; set; }
    public HostFunction? TypeErrorConstructor { get; set; }
    public HostFunction? RangeErrorConstructor { get; set; }
    public JsObject? BooleanPrototype { get; set; }
    public JsObject? NumberPrototype { get; set; }
    public JsObject? StringPrototype { get; set; }
    public HostFunction? ArrayConstructor { get; set; }
}

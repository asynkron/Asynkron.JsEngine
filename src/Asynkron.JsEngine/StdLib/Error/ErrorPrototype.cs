using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Error", ToStringTag = "Error")]
public sealed partial class ErrorPrototype : JsPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        if (!thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            throw ThrowTypeError("Error.prototype.toString called on non-object", realm: Realm);
        }

        var hasName = accessor.TryGetProperty("name", out var nameValue);
        var hasMessage = accessor.TryGetProperty("message", out var messageValue);

        // nameValue and messageValue are JsValue structs - use IsUndefined to check
        var nameString = !hasName || nameValue.IsUndefined
            ? "Error"
            : JsOps.ToJsString(nameValue);
        var messageString = !hasMessage || messageValue.IsUndefined
            ? string.Empty
            : JsOps.ToJsString(messageValue);

        if (nameString.Length == 0)
        {
            return new JsValue(messageString);
        }

        if (messageString.Length == 0)
        {
            return new JsValue(nameString);
        }

        return new JsValue($"{nameString}: {messageString}");
    }

    protected override void ConfigurePrototype()
    {
        Prototype.DefineProperty("name",
            new PropertyDescriptor { Value = "Error", Writable = true, Enumerable = false, Configurable = true });
        Prototype.DefineProperty("message",
            new PropertyDescriptor { Value = string.Empty, Writable = true, Enumerable = false, Configurable = true });
    }
}

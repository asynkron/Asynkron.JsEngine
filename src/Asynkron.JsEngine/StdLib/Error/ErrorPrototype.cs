using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Error", ToStringTag = "Error")]
public sealed partial class ErrorPrototype : JsPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public object? ToString(JsValue thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is not IJsPropertyAccessor accessor)
        {
            throw ThrowTypeError("Error.prototype.toString called on non-object", realm: Realm);
        }

        var hasName = accessor.TryGetProperty("name", out var nameValue);
        var hasMessage = accessor.TryGetProperty("message", out var messageValue);

        var nameString = !hasName || ReferenceEquals(nameValue, Symbol.Undefined)
            ? "Error"
            : JsOps.ToJsString(nameValue);
        var messageString = !hasMessage || ReferenceEquals(messageValue, Symbol.Undefined)
            ? string.Empty
            : JsOps.ToJsString(messageValue);

        if (nameString.Length == 0)
        {
            return messageString;
        }

        if (messageString.Length == 0)
        {
            return nameString;
        }

        return $"{nameString}: {messageString}";
    }

    protected override void ConfigurePrototype()
    {
        Prototype.DefineProperty("name",
            new PropertyDescriptor { Value = "Error", Writable = true, Enumerable = false, Configurable = true });
        Prototype.DefineProperty("message",
            new PropertyDescriptor { Value = string.Empty, Writable = true, Enumerable = false, Configurable = true });
    }
}

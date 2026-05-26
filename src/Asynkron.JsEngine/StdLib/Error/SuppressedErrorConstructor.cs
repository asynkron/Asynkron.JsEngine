#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// SuppressedError(error, suppressed, message)
/// Per ES2025 explicit-resource-management spec:
/// - error: the error that replaced the suppressed error
/// - suppressed: the error that was suppressed
/// - message: optional error message
/// </summary>
[JsConstructor("SuppressedError", PrototypeType = typeof(ErrorPrototype), Length = 3d,
    DisplayName = "SuppressedError")]
public sealed partial class SuppressedErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "SuppressedError";

    /// <summary>
    /// SuppressedError(error, suppressed[, message[, options]])
    /// args[0] = error, args[1] = suppressed, args[2] = message, args[3] = options
    /// </summary>
    protected override void InitializeError(JsObject instance, IReadOnlyList<JsValue> args)
    {
        var errorArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var suppressedArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var messageArg = args.Count > 2 ? args[2] : JsValue.Undefined;
        var optionsArg = args.Count > 3 ? args[3] : JsValue.Undefined;
        InitializeErrorShared(instance, messageArg, optionsArg);

        // Per spec: Set error and suppressed properties
        instance.DefineProperty("error",
            new PropertyDescriptor
            {
                JsValue = errorArg, Writable = true, Enumerable = false, Configurable = true
            });
        instance.DefineProperty("suppressed",
            new PropertyDescriptor
            {
                JsValue = suppressedArg, Writable = true, Enumerable = false, Configurable = true
            });
    }
}

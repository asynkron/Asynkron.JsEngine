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
        instance.RealmState ??= Realm;

        // Set [[ErrorData]] internal slot
        instance.DefineProperty("_errorData",
            new PropertyDescriptor
            {
                Value = JsValue.True, Writable = false, Enumerable = false, Configurable = false
            });

        var errorArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var suppressedArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var messageArg = args.Count > 2 ? args[2] : JsValue.Undefined;
        var optionsArg = args.Count > 3 ? args[3] : JsValue.Undefined;

        // Set message if provided and not undefined
        if (!messageArg.IsUndefined)
        {
            var message = messageArg.IsNull ? "null" : JsOps.ToJsString(messageArg);
            instance.DefineProperty("message",
                new PropertyDescriptor
                {
                    Value = message, Writable = true, Enumerable = false, Configurable = true
                });
        }

        // ES2022: InstallErrorCause - handle options.cause
        if (optionsArg.IsObject)
        {
            if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var optionsAccessor) &&
                StandardLibrary.HasProperty(optionsAccessor, "cause"))
            {
                JsOps.TryGetPropertyValue(optionsArg, "cause", out var cause);
                instance.DefineProperty("cause",
                    new PropertyDescriptor
                    {
                        Value = cause, Writable = true, Enumerable = false, Configurable = true
                    });
            }
        }

        // Per spec: Set error and suppressed properties
        instance.DefineProperty("error",
            new PropertyDescriptor
            {
                Value = errorArg, Writable = true, Enumerable = false, Configurable = true
            });
        instance.DefineProperty("suppressed",
            new PropertyDescriptor
            {
                Value = suppressedArg, Writable = true, Enumerable = false, Configurable = true
            });
    }
}

#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Error", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "Error")]
public sealed partial class ErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "Error";

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        base.ConfigureConstructor(constructor);

        // Error.isError(arg) — returns true if arg has [[ErrorData]] internal slot
        var isErrorFn = new HostFunction(
            (_, args) =>
            {
                var arg = args.GetArgument(0);
                if (!arg.TryGetObject<JsObject>(out var obj))
                {
                    return JsValue.False;
                }

                // Check for _errorData property (set by ErrorConstructorBase.InitializeError)
                return obj.GetOwnPropertyDescriptor("_errorData") is not null
                    ? JsValue.True
                    : JsValue.False;
            }, Realm, false);
        isErrorFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = (JsValue)"isError", Writable = false, Enumerable = false, Configurable = true
        });
        isErrorFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = JsValue.FromDouble(1), Writable = false, Enumerable = false, Configurable = true
        });
        constructor.SetHostedProperty("isError", isErrorFn, Realm);
    }
}

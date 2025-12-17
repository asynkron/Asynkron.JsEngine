using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ReferenceError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "ReferenceError")]
public sealed partial class ReferenceErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "ReferenceError";
}

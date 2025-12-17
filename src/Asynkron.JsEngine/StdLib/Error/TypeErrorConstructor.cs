using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("TypeError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "TypeError")]
public sealed partial class TypeErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "TypeError";
}

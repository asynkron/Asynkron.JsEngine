#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("SyntaxError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "SyntaxError")]
public sealed partial class SyntaxErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "SyntaxError";
}

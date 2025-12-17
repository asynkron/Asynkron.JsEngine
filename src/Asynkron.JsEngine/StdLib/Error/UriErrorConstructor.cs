using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("URIError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "URIError")]
public sealed partial class UriErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "URIError";
}

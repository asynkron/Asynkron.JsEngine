using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

internal interface IHomeObjectConfigurableCallable
{
    void SetHomeObject(IJsObjectLike homeObject);

    void DisableConstruction();
}

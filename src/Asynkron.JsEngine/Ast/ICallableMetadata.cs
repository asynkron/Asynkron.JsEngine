using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public interface ICallableMetadata
{
    bool IsArrowFunction { get; }

    bool DisallowConstruct { get; }

    RealmState RealmState { get; }
}

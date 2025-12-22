#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.Ast;

public interface ICallableMetadata
{
    bool IsArrowFunction { get; }

    bool DisallowConstruct { get; }

    RealmState RealmState { get; }
}

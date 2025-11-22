using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

internal enum IteratorDriverKind
{
    Sync,
    Await
}

internal sealed record IteratorDriverPlan(
    IteratorDriverKind Kind,
    ExpressionNode Iterable,
    BindingTarget Target,
    VariableKind? DeclarationKind,
    BlockStatement Body);

internal sealed class IteratorDriverState
{
    public JsObject? IteratorObject { get; init; }
    public IEnumerator<object?>? Enumerator { get; init; }
    public bool IsAsyncIterator { get; init; }
    public bool AwaitingNextResult { get; set; }
    public bool AwaitingValue { get; set; }
}

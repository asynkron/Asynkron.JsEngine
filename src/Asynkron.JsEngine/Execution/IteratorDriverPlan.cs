using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal sealed record IteratorDriverPlan(
    IteratorDriverKind Kind,
    ExpressionNode Iterable,
    BindingTarget Target,
    VariableKind? DeclarationKind,
    BlockStatement Body);

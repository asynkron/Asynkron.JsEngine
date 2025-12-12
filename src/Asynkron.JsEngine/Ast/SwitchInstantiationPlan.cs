using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

internal readonly record struct SwitchLexicalBinding(BindingTarget Target, bool IsConst);

internal readonly record struct SwitchFunctionBinding(Symbol Name, FunctionExpression Function, bool InitializeNow);

internal sealed record SwitchInstantiationPlan(
    bool IsStrict,
    ImmutableArray<SwitchLexicalBinding> LexicalBindings,
    ImmutableArray<SwitchFunctionBinding> FunctionBindings,
    ImmutableArray<Symbol> ClassBindings);


using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

internal sealed record SwitchInstantiationPlan(
    bool IsStrict,
    ImmutableArray<SwitchLexicalBinding> LexicalBindings,
    ImmutableArray<SwitchFunctionBinding> FunctionBindings,
    ImmutableArray<Symbol> ClassBindings);


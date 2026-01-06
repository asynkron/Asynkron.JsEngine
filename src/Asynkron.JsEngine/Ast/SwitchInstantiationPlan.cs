#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Ast;

internal sealed record SwitchInstantiationPlan(
    ImmutableArray<SwitchLexicalBinding> LexicalBindings,
    ImmutableArray<SwitchFunctionBinding> FunctionBindings,
    ImmutableArray<Symbol> ClassBindings);

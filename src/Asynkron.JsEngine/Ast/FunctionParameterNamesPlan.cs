using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

internal sealed class FunctionParameterNamesPlan
{
    internal ImmutableArray<Symbol> ParameterNames { get; }

    private FunctionParameterNamesPlan(ImmutableArray<Symbol> parameterNames)
    {
        ParameterNames = parameterNames;
    }

    internal static FunctionParameterNamesPlan Build(FunctionExpression function)
    {
        var parameterNames = new List<Symbol>();
        function.CollectParameterNamesFromFunction(parameterNames);
        var template = parameterNames.Count == 0
            ? ImmutableArray<Symbol>.Empty
            : [..parameterNames];
        return new FunctionParameterNamesPlan(template);
    }
}

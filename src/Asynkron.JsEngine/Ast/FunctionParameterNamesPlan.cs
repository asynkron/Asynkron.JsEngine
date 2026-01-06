#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Ast;

internal sealed class FunctionParameterNamesPlan
{
    private FunctionParameterNamesPlan(ImmutableArray<Symbol> parameterNames)
    {
        ParameterNames = parameterNames;
    }

    internal ImmutableArray<Symbol> ParameterNames { get; }

    internal static FunctionParameterNamesPlan Build(FunctionExpression function)
    {
        var parameterNames = new List<Symbol>();
        function.CollectParameterNamesFromFunction(parameterNames);
        var template = parameterNames.Count == 0
            ? ImmutableArray<Symbol>.Empty
            : [.. parameterNames];
        return new FunctionParameterNamesPlan(template);
    }
}

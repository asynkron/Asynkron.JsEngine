#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static int GetExpectedParameterCount(this ImmutableArray<FunctionParameter> parameters)
    {
        var count = 0;
        foreach (var parameter in parameters)
        {
            if (parameter.IsRest || parameter.DefaultValue is not null)
            {
                break;
            }

            count++;
        }

        return count;
    }
}

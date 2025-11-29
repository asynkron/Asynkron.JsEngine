using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(ImmutableArray<FunctionParameter> parameters)
    {
        private int GetExpectedParameterCount()
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

}

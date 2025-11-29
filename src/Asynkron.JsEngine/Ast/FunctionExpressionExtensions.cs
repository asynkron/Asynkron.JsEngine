using System.Collections.Generic;
using System.Linq;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(FunctionExpression function)
    {
        private JsArgumentsObject CreateArgumentsObject(
            IReadOnlyList<object?> arguments,
            JsEnvironment environment,
            RealmState realmState,
            IJsCallable? callee)
        {
            var values = new object?[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                values[i] = arguments[i];
            }

            var mapped = !function.Body.IsStrict && IsSimpleParameterList(function);
            var mappedParameters = new Symbol?[arguments.Count];
            if (mapped)
            {
                var parameterSymbols = function.Parameters
                    .Where(p => p is { IsRest: false, Pattern: null, DefaultValue: null, Name: not null })
                    .Select(p => p.Name!)
                    .ToArray();

                for (var i = 0; i < mappedParameters.Length && i < parameterSymbols.Length; i++)
                {
                    mappedParameters[i] = parameterSymbols[i];
                }
            }

            return new JsArgumentsObject(
                values,
                mappedParameters,
                environment,
                mapped,
                realmState,
                callee,
                function.Body.IsStrict);
        }

        private bool IsSimpleParameterList()
        {
            foreach (var parameter in function.Parameters)
            {
                if (parameter.IsRest || parameter.Pattern is not null || parameter.DefaultValue is not null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

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

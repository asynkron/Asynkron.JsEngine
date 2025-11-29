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

extension(IdentifierExpression identifier)
    {
        private object? EvaluateIdentifier(JsEnvironment environment,
            EvaluationContext context)
        {
            var reference = AssignmentReferenceResolver.Resolve(identifier, environment, context, EvaluateExpression);
            try
            {
                return reference.GetValue();
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
            {
                object? errorObject = ex.Message;

                if (environment.TryGet(Symbol.Intern("ReferenceError"), out var ctor) &&
                    ctor is IJsCallable callable)
                {
                    try
                    {
                        errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
                    }
                    catch (ThrowSignal signal)
                    {
                        errorObject = signal.ThrownValue;
                    }
                }

                context.SetThrow(errorObject);
                return errorObject;
            }
        }
    }

}

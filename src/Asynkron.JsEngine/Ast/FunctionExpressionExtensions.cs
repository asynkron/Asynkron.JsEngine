using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

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

    extension(FunctionExpression functionExpression)
    {
        private IJsCallable CreateFunctionValue(JsEnvironment environment,
            EvaluationContext context)
        {
            return functionExpression.IsGenerator switch
            {
                true when functionExpression.IsAsync => new AsyncGeneratorFactory(functionExpression, environment,
                    context.RealmState),
                true => new TypedGeneratorFactory(functionExpression, environment, context.RealmState),
                _ => new TypedFunction(functionExpression, environment, context.RealmState)
            };
        }
    }

    extension(FunctionExpression function)
    {
        private void CollectParameterNamesFromFunction(List<Symbol> names)
        {
            foreach (var parameter in function.Parameters)
            {
                if (parameter.Name is not null)
                {
                    names.Add(parameter.Name);
                }

                if (parameter.Pattern is not null)
                {
                    WalkBindingTargets(parameter.Pattern, id => names.Add(id.Name));
                }
            }
        }
    }

    extension(FunctionExpression function)
    {
        private void BindFunctionParameters(IReadOnlyList<object?> arguments,
            JsEnvironment environment, EvaluationContext context)
        {
            var parameterNames = new List<Symbol>();
            foreach (var parameter in function.Parameters)
            {
                CollectParameterNames(parameter, parameterNames);
            }

            foreach (var name in parameterNames)
            {
                environment.Define(name, JsEnvironment.Uninitialized, isLexical: false,
                    blocksFunctionScopeOverride: true);
            }

            var argumentIndex = 0;

            foreach (var parameter in function.Parameters)
            {
                if (parameter.IsRest)
                {
                    var restArray = new JsArray(context.RealmState);
                    for (; argumentIndex < arguments.Count; argumentIndex++)
                    {
                        restArray.Push(arguments[argumentIndex]);
                    }

                    if (parameter.Pattern is not null)
                    {
                        ApplyBindingTarget(parameter.Pattern, restArray, environment, context,
                            BindingMode.DefineParameter);
                        if (context.ShouldStopEvaluation)
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (parameter.Name is null)
                        {
                            throw new InvalidOperationException("Rest parameter must have an identifier.");
                        }

                        environment.Define(parameter.Name, restArray, isLexical: false);
                    }

                    continue;
                }

                var value = argumentIndex < arguments.Count ? arguments[argumentIndex] : Symbol.Undefined;
                argumentIndex++;

                if (ReferenceEquals(value, Symbol.Undefined) && parameter.DefaultValue is not null)
                {
                    if (parameter.Name is not null &&
                        DefaultReferencesParameter(parameter.DefaultValue, parameter.Name))
                    {
                        var error = StandardLibrary.ThrowReferenceError(
                            $"{parameter.Name.Name} is not initialized", context, context.RealmState);
                        context.SetThrow(error.ThrownValue);
                        return;
                    }

                    value = EvaluateExpression(parameter.DefaultValue, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (parameter.Pattern is not null)
                {
                    ApplyBindingTarget(parameter.Pattern, value, environment, context, BindingMode.DefineParameter);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    continue;
                }

                if (parameter.Name is null)
                {
                    throw new InvalidOperationException(
                        "Parameter must have an identifier when no pattern is provided.");
                }

                environment.Define(parameter.Name, value, isLexical: false);
            }

            return;

            static bool DefaultReferencesParameter(ExpressionNode expression, Symbol parameterName)
            {
                switch (expression)
                {
                    case IdentifierExpression ident:
                        return ReferenceEquals(ident.Name, parameterName);
                    case AssignmentExpression assign:
                        return ReferenceEquals(assign.Target, parameterName) ||
                               DefaultReferencesParameter(assign.Value, parameterName);
                    case BinaryExpression binary:
                        return DefaultReferencesParameter(binary.Left, parameterName) ||
                               DefaultReferencesParameter(binary.Right, parameterName);
                    case ConditionalExpression cond:
                        return DefaultReferencesParameter(cond.Test, parameterName) ||
                               DefaultReferencesParameter(cond.Consequent, parameterName) ||
                               DefaultReferencesParameter(cond.Alternate, parameterName);
                    case CallExpression call:
                        return DefaultReferencesParameter(call.Callee, parameterName) ||
                               call.Arguments.Any(arg => DefaultReferencesParameter(arg.Expression, parameterName));

                    case MemberExpression member:
                        return DefaultReferencesParameter(member.Target, parameterName) ||
                               DefaultReferencesParameter(member.Property, parameterName);
                    case UnaryExpression unary:
                        return DefaultReferencesParameter(unary.Operand, parameterName);
                    case SequenceExpression seq:
                        return DefaultReferencesParameter(seq.Left, parameterName) ||
                               DefaultReferencesParameter(seq.Right, parameterName);
                    case ArrayExpression arr:
                        foreach (var element in arr.Elements)
                        {
                            if (element.Expression is not null &&
                                DefaultReferencesParameter(element.Expression, parameterName))
                            {
                                return true;
                            }
                        }

                        return false;
                    case ObjectExpression obj:
                        foreach (var member in obj.Members)
                        {
                            if (member.Value is not null &&
                                DefaultReferencesParameter(member.Value, parameterName))
                            {
                                return true;
                            }

                            if (member.Function is not null &&
                                DefaultReferencesParameter(member.Function, parameterName))
                            {
                                return true;
                            }
                        }

                        return false;
                    case TemplateLiteralExpression template:
                        foreach (var part in template.Parts)
                        {
                            if (part.Expression is not null &&
                                DefaultReferencesParameter(part.Expression, parameterName))
                            {
                                return true;
                            }
                        }

                        return false;
                    case TaggedTemplateExpression tagged:
                        return DefaultReferencesParameter(tagged.Tag, parameterName) ||
                               DefaultReferencesParameter(tagged.StringsArray, parameterName) ||
                               DefaultReferencesParameter(tagged.RawStringsArray, parameterName) ||
                               tagged.Expressions.Any(expr => DefaultReferencesParameter(expr, parameterName));
                    case YieldExpression { Expression: not null } yieldExpression:
                        return DefaultReferencesParameter(yieldExpression.Expression, parameterName);
                    case AwaitExpression awaitExpression:
                        return DefaultReferencesParameter(awaitExpression.Expression, parameterName);
                    case FunctionExpression:
                        // Nested functions have their own scope; references to the parameter name
                        // do not count towards self-referential defaults here.
                        return false;
                    default:
                        return false;
                }
            }

            static void CollectParameterNames(FunctionParameter parameter, List<Symbol> names)
            {
                if (parameter.Name is not null)
                {
                    names.Add(parameter.Name);
                }

                if (parameter.Pattern is not null)
                {
                    CollectBindingNames(parameter.Pattern, names);
                }
            }

            static void CollectBindingNames(BindingTarget target, List<Symbol> names)
            {
                while (true)
                {
                    switch (target)
                    {
                        case IdentifierBinding identifier:
                            names.Add(identifier.Name);
                            break;
                        case ArrayBinding arrayBinding:
                            foreach (var element in arrayBinding.Elements)
                            {
                                if (element.Target is not null)
                                {
                                    CollectBindingNames(element.Target, names);
                                }
                            }

                            if (arrayBinding.RestElement is not null)
                            {
                                target = arrayBinding.RestElement;
                                continue;
                            }

                            break;
                        case ObjectBinding objectBinding:
                            foreach (var property in objectBinding.Properties)
                            {
                                CollectBindingNames(property.Target, names);
                            }

                            if (objectBinding.RestElement is not null)
                            {
                                target = objectBinding.RestElement;
                                continue;
                            }

                            break;
                        case AssignmentTargetBinding:
                            // Assignment targets do not declare new bindings in parameter lists.
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported binding target '{target.GetType().Name}'.");
                    }

                    break;
                }
            }
        }
    }

    extension(FunctionExpression function)
    {
        private bool HasParameterExpressions()
        {
            foreach (var parameter in function.Parameters)
            {
                if (parameter.DefaultValue is not null)
                {
                    return true;
                }

                if (parameter.Pattern is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

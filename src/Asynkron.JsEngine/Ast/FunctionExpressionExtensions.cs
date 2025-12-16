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
            IJsCallable? callee,
            bool isStrict)
        {
            var values = new object?[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                values[i] = arguments[i];
            }

            var mapped = !isStrict && IsSimpleParameterList(function);
            var mappedParameters = new Symbol?[arguments.Count];
            if (mapped)
            {
                var parameterSymbols = function.Parameters
                    .Where(p => p is { IsRest: false, Pattern: null, DefaultValue: null, Name: not null })
                    .Select(p => p.Name!)
                    .ToArray();

                var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
                for (var i = Math.Min(mappedParameters.Length, parameterSymbols.Length) - 1; i >= 0; i--)
                {
                    var symbol = parameterSymbols[i];
                    if (symbol is null || !seen.Add(symbol))
                    {
                        continue;
                    }

                    mappedParameters[i] = symbol;
                }
            }

            return new JsArgumentsObject(
                values,
                mappedParameters,
                environment,
                mapped,
                realmState,
                callee,
                isStrict);
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
                environment.DefineJsValue(name, JsValue.Uninitialized, isLexical: false,
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

                        environment.DefineJsValue(parameter.Name, JsValue.FromObjectUnsafe(restArray), isLexical: false);
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

                    value = EvaluateExpression(parameter.DefaultValue, environment, context).ToObject();
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

                environment.DefineJsValue(parameter.Name, JsValue.FromObjectUnsafe(value), isLexical: false);
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

    extension(FunctionExpression functionExpression)
    {
        private IJsCallable CreateFunctionValue(JsEnvironment environment,
            EvaluationContext context,
            bool createFunctionNameEnvironment = false,
            bool isConstructorFunction = true,
            bool skipInternalNameBinding = false)
        {
            var closureEnvironment = environment;
            JsEnvironment? functionNameEnvironment = null;
            if (createFunctionNameEnvironment &&
                functionExpression.Name is { } functionName &&
                !functionExpression.IsArrow)
            {
                functionNameEnvironment = new JsEnvironment(
                    environment,
                    isFunctionScope: false,
                    isStrict: context.CurrentScope.IsStrict,
                    creatingSource: functionExpression.Source,
                    description: $"FunctionExpression:{functionName.Name}");
                closureEnvironment = functionNameEnvironment;
            }

            // For function declarations, the name binding is in the outer scope (mutable var),
            // so we pass hasFunctionNameEnvironment: true to skip the internal const binding.
            // For named function expressions with createFunctionNameEnvironment, we also skip
            // the internal binding since the wrapper environment handles it.
            var hasFunctionNameEnvironment = functionNameEnvironment is not null || skipInternalNameBinding;

            IJsCallable callable = functionExpression.IsGenerator switch
            {
                true when functionExpression.IsAsync => new AsyncGeneratorFactory(functionExpression,
                    closureEnvironment,
                    context.RealmState, context.CurrentScope.IsStrict, hasFunctionNameEnvironment, isConstructorFunction),
                true => new TypedGeneratorFactory(functionExpression, closureEnvironment, context.RealmState,
                    context.CurrentScope.IsStrict, hasFunctionNameEnvironment, isConstructorFunction),
                _ => new TypedFunction(functionExpression, closureEnvironment, context.RealmState,
                    context.CurrentScope.IsStrict, hasFunctionNameEnvironment, isConstructorFunction)
            };

            var capturedPrivateScopes = context.CapturePrivateNameScopes();
            switch (callable)
            {
                case TypedFunction typed when context.CurrentPrivateNameScope is not null &&
                                              typed.PrivateNameScope is null:
                    typed.SetPrivateNameScope(context.CurrentPrivateNameScope);
                    typed.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                    break;
                case TypedFunction typed:
                    typed.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                    break;
                case TypedGeneratorFactory generatorFactory when context.CurrentPrivateNameScope is not null &&
                                                                 generatorFactory.PrivateNameScope is null:
                    generatorFactory.SetPrivateNameScope(context.CurrentPrivateNameScope);
                    generatorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                    break;
                case TypedGeneratorFactory generatorFactory:
                    generatorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                    break;
                case AsyncGeneratorFactory asyncGeneratorFactory when context.CurrentPrivateNameScope is not null &&
                                                                      asyncGeneratorFactory.PrivateNameScope is null:
                    asyncGeneratorFactory.SetPrivateNameScope(context.CurrentPrivateNameScope);
                    asyncGeneratorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                    break;
                case AsyncGeneratorFactory asyncGeneratorFactory:
                    asyncGeneratorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                    break;
            }

            if (functionNameEnvironment is not null)
            {
                // Named function expression bindings are immutable but silently fail
                // assignment in non-strict mode (unlike const which always throws)
                functionNameEnvironment.DefineJsValue(functionExpression.Name!, JsValue.FromObjectUnsafe(callable),
                    isConst: false,
                    isLexical: true,
                    blocksFunctionScopeOverride: true,
                    isImmutableBinding: true);
            }

            // Per ES spec 13.3.1.4: If IsAnonymousFunctionDefinition(Initializer) is true and
            // hasNameProperty is false, perform SetFunctionName(value, bindingId).
            if (functionExpression.Name is null &&
                context.CurrentFunctionNameHint is { } inferredName &&
                callable is IFunctionNameTarget nameTarget)
            {
                nameTarget.EnsureHasName(inferredName.Name);
            }

            return callable;
        }
    }
}

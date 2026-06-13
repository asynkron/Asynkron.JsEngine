using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsArgumentsObject CreateArgumentsObject(this FunctionExpression function, IReadOnlyList<JsValue> arguments,
        JsEnvironment environment,
        RealmState realmState,
        IJsCallable? callee,
        bool isStrict)
    {
        var mapped = !isStrict && function.IsSimpleParameterList();
        var mappedParameters = new Symbol?[arguments.Count];
        if (!mapped)
        {
            return new JsArgumentsObject(
                arguments,
                mappedParameters,
                environment,
                mapped,
                realmState,
                callee,
                isStrict);
        }

        // Collect simple parameter symbols in a single pass (avoids LINQ allocations)
        var parameterSymbols = new Symbol[function.Parameters.Length];
        var symbolCount = 0;
        foreach (var p in function.Parameters)
        {
            if (p is { IsRest: false, Pattern: null, DefaultValue: null, Name: not null })
            {
                parameterSymbols[symbolCount++] = p.Name;
            }
        }

        var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        for (var i = Math.Min(mappedParameters.Length, symbolCount) - 1; i >= 0; i--)
        {
            var symbol = parameterSymbols[i];
            if (!seen.Add(symbol))
            {
                continue;
            }

            mappedParameters[i] = symbol;
        }

        return new JsArgumentsObject(
            arguments,
            mappedParameters,
            environment,
            mapped,
            realmState,
            callee,
            isStrict);
    }

    private static bool IsSimpleParameterList(this FunctionExpression function)
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

    public static void CollectParameterNamesFromFunction(this FunctionExpression function, List<Symbol> names)
    {
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Name is not null)
            {
                names.Add(parameter.Name);
            }

            parameter.Pattern?.WalkBindingTargets(id => names.Add(id.Name));
        }
    }

    private static void BindFunctionParameters(this FunctionExpression function, IReadOnlyList<JsValue> arguments,
        JsEnvironment environment, EvaluationContext context)
    {
        var parameterNames = new List<Symbol>();
        foreach (var parameter in function.Parameters)
        {
            CollectParameterNames(parameter, parameterNames);
        }

        foreach (var name in parameterNames)
        {
            environment.DefineJsValue(name, JsValue.Uninitialized, isLexicalBinding: false,
                blocksFunctionScopeOverride: true);
            environment.RealmState?.Logger?.LogInformation(
                "Param hoist name={Name} envScope={ScopeId}",
                name.Name,
                environment.ScopeId);
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
                    parameter.Pattern.ApplyBindingTarget(JsValue.FromJsArray(restArray), environment, context,
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

                    environment.DefineJsValue(parameter.Name, JsValue.FromJsArray(restArray),
                        isLexicalBinding: false);
                }

                continue;
            }

            var value = argumentIndex < arguments.Count ? arguments[argumentIndex] : JsValue.Undefined;
            argumentIndex++;

            if (value.IsUndefined && parameter.DefaultValue is not null)
            {
                if (parameter.Name is not null &&
                    DefaultReferencesParameter(parameter.DefaultValue, parameter.Name))
                {
                    var error = StandardLibrary.ThrowReferenceError(
                        $"{parameter.Name.Name} is not initialized", context, context.RealmState);
                    context.SetThrow(error.ThrownValue);
                    return;
                }

                value = ((IAstCacheable<LoweredExpressionProgramCache>)parameter)
                    .GetOrCreateCache()
                    .Execute(environment, context, "Function parameter default expression");
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            if (parameter.Pattern is not null)
            {
                parameter.Pattern.ApplyBindingTarget(value, environment, context, BindingMode.DefineParameter);
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

            environment.DefineJsValue(parameter.Name, value, isLexicalBinding: false);
            environment.RealmState?.Logger?.LogInformation(
                "Param bind name={Name} envScope={ScopeId} valueKind={Kind}",
                parameter.Name.Name,
                environment.ScopeId,
                value.Kind);
        }

        return;

        static bool DefaultReferencesParameter(ExpressionNode expression, Symbol parameterName)
        {
            while (true)
            {
                switch (expression)
                {
                    case IdentifierExpression ident:
                        {
                            return ReferenceEquals(ident.Name, parameterName);
                        }
                    case AssignmentExpression assign:
                        {
                            return ReferenceEquals(assign.Target, parameterName) ||
                                   DefaultReferencesParameter(assign.Value, parameterName);
                        }
                    case BinaryExpression binary:
                        {
                            return DefaultReferencesParameter(binary.Left, parameterName) ||
                                   DefaultReferencesParameter(binary.Right, parameterName);
                        }
                    case ConditionalExpression cond:
                        {
                            return DefaultReferencesParameter(cond.Test, parameterName) ||
                                   DefaultReferencesParameter(cond.Consequent, parameterName) ||
                                   DefaultReferencesParameter(cond.Alternate, parameterName);
                        }
                    case CallExpression call:
                        {
                            if (DefaultReferencesParameter(call.Callee, parameterName))
                            {
                                return true;
                            }

                            foreach (var arg in call.Arguments)
                            {
                                if (DefaultReferencesParameter(arg.Expression, parameterName))
                                {
                                    return true;
                                }
                            }

                            return false;
                        }

                    case MemberExpression member:
                        {
                            return DefaultReferencesParameter(member.Target, parameterName) ||
                                   DefaultReferencesParameter(member.Property, parameterName);
                        }
                    case UnaryExpression unary:
                        {
                            expression = unary.Operand;
                            continue;
                        }
                    case SequenceExpression seq:
                        {
                            return DefaultReferencesParameter(seq.Left, parameterName) ||
                                   DefaultReferencesParameter(seq.Right, parameterName);
                        }
                    case ArrayExpression arr:
                        {
                            foreach (var element in arr.Elements)
                            {
                                if (element.Expression is not null &&
                                    DefaultReferencesParameter(element.Expression, parameterName))
                                {
                                    return true;
                                }
                            }

                            return false;
                        }
                    case ObjectExpression obj:
                        {
                            foreach (var member in obj.Members)
                            {
                                if (member.Value is not null && DefaultReferencesParameter(member.Value, parameterName))
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
                        }
                    case TemplateLiteralExpression template:
                        {
                            foreach (var part in template.Parts)
                            {
                                if (part.Expression is not null &&
                                    DefaultReferencesParameter(part.Expression, parameterName))
                                {
                                    return true;
                                }
                            }

                            return false;
                        }
                    case TaggedTemplateExpression tagged:
                        {
                            if (DefaultReferencesParameter(tagged.Tag, parameterName) ||
                                DefaultReferencesParameter(tagged.StringsArray, parameterName) ||
                                DefaultReferencesParameter(tagged.RawStringsArray, parameterName))
                            {
                                return true;
                            }

                            foreach (var expr in tagged.Expressions)
                            {
                                if (DefaultReferencesParameter(expr, parameterName))
                                {
                                    return true;
                                }
                            }

                            return false;
                        }
                    case YieldExpression { Expression: not null } yieldExpression:
                        {
                            expression = yieldExpression.Expression;
                            continue;
                        }
                    case AwaitExpression awaitExpression:
                        {
                            expression = awaitExpression.Expression;
                            continue;
                        }
                    case FunctionExpression:
                        {
                            // Nested functions have their own scope; references to the parameter name
                            // do not count towards self-referential defaults here.
                            return false;
                        }
                    default:
                        {
                            return false;
                        }
                }
            }
        }

        static void CollectParameterNames(FunctionParameter parameter, List<Symbol> names)
        {
            if (parameter.Name is not null)
            {
                names.Add(parameter.Name);
            }

            // Note: AssignmentTargetBinding is silently skipped by CollectSymbolsFromBinding
            // (it doesn't declare new bindings in parameter lists)
            parameter.Pattern?.CollectSymbolsFromBinding(names);
        }
    }

    private static bool HasParameterExpressions(this FunctionExpression function)
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

    /// <summary>
    ///     True when every formal parameter is a plain identifier with no destructuring pattern,
    ///     default initializer, or rest element. The unified-bytecode resumable route copies arguments
    ///     directly into positional slots (<c>PopulateParameterSlots</c>) and cannot perform
    ///     IteratorBindingInitialization, whose destructuring side effects and eager errors (e.g.
    ///     <c>GetIterator(null)</c> on a generator/async call) must run before the resumable object is
    ///     produced. Non-simple parameter lists must therefore fall back to the runner path.
    /// </summary>
    internal static bool HasOnlySimpleIdentifierParameters(this FunctionExpression function)
    {
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Name is null ||
                parameter.Pattern is not null ||
                parameter.DefaultValue is not null ||
                parameter.IsRest)
            {
                return false;
            }
        }

        return true;
    }

    private static FunctionExecutionPlanSeed ResolveCallablePlanSeed(
        FunctionExpression functionExpression,
        JsEnvironment environment,
        FunctionExecutionPlanSeed planSeed)
    {
        if (!functionExpression.IsAsync && !functionExpression.IsGenerator)
        {
            var planCache = ((IAstCacheable<ExecutionPlanCache>)functionExpression).GetOrCreateCache();
            if (planSeed.Succeeded && !ReferenceEquals(planSeed.Plan, planCache.Plan))
            {
                planSeed = FunctionExecutionPlanSeed.FromCache(planCache);
            }
            else if (!planSeed.Succeeded && planSeed.Failure is null)
            {
                planSeed = FunctionExecutionPlanSeed.FromCache(planCache);
            }
        }

        if (functionExpression.IsAsync ||
            functionExpression.IsGenerator ||
            !environment.HasWithObjectInChain())
        {
            return planSeed;
        }

        if (!planSeed.Succeeded)
        {
            return planSeed;
        }

        // Sync callables closed over an active with-chain can still execute through the
        // no-slot IR runner. Keep the cached plan/failure result intact instead of forcing
        // them back onto legacy AST execution.
        return planSeed;
    }

    internal static JsValue CreateFunctionLiteralValue(
        FunctionLiteralDescriptor descriptor,
        JsEnvironment environment,
        EvaluationContext context,
        bool isConstructorFunction)
    {
        return JsValue.FromObjectUnsafe(
            descriptor.Function.CreateFunctionValue(
                environment,
                context,
                isConstructorFunction,
                planSeed: descriptor.PlanSeed));
    }

    private static IJsCallable CreateFunctionValue(this FunctionExpression functionExpression, JsEnvironment environment,
        EvaluationContext context,
        bool isConstructorFunction = true,
        bool skipInternalNameBinding = false,
        FunctionExecutionPlanSeed planSeed = default)
    {
        var closureEnvironment = environment;
        JsEnvironment? functionNameEnvironment = null;

        // For named function expressions, create an intermediate scope for the function name.
        // Per ECMAScript spec, the function name is bound in a scope between the outer scope
        // and the parameter scope, so it's visible inside but not outside.
        // This environment uses slot-based storage with the ScopeId from scope analysis.
        var hasFunctionNameEnvironment = skipInternalNameBinding;
        if (!skipInternalNameBinding && functionExpression.Name is not null &&
            functionExpression is { IsArrow: false })
        {
            functionNameEnvironment = JsEnvironment.CreateInstance(environment);
            // Use scope ID from analysis if available, otherwise use a fallback that still allows lookup
            if (functionExpression.FunctionNameScopeId >= 0)
            {
                functionNameEnvironment.ScopeId = functionExpression.FunctionNameScopeId;
                functionNameEnvironment.InitializeSlots(1); // Only one slot for the function name
            }

            // Without scope analysis, the function name will be looked up via dictionary
            closureEnvironment = functionNameEnvironment;
            hasFunctionNameEnvironment = true;
        }

        // Per ES spec, a function's strictness is determined by:
        // 1. The function body contains "use strict" directive (function.Body.IsStrict)
        // 2. The function is defined in strict mode code (closureEnvironment.IsStrict)
        // We use closureEnvironment.IsStrict instead of context.CurrentScope.IsStrict because
        // the scope stack may not accurately reflect the lexical strictness during function creation.
        var isLexicallyStrict = closureEnvironment.IsStrict;

        // Mark the closure environment as captured - it cannot be returned to the pool
        // since this function holds a reference to it
        closureEnvironment.Capture();

        planSeed = ResolveCallablePlanSeed(functionExpression, closureEnvironment, planSeed);

        IJsCallable callable = functionExpression.IsGenerator switch
        {
            true when functionExpression.IsAsync => new AsyncGeneratorFunctionInvoker(functionExpression,
                closureEnvironment,
                context.RealmState, isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction, planSeed),
            true when ShouldCreateIrSyncGeneratorInvoker(functionExpression, closureEnvironment, planSeed) =>
                new IrSyncGeneratorInvoker(functionExpression, closureEnvironment, context.RealmState,
                    isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction, planSeed),
            true => new SyncGeneratorInvoker(functionExpression, closureEnvironment, context.RealmState,
                isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction, planSeed),
            _ => new SyncFunctionInvoker(functionExpression, closureEnvironment, context.RealmState,
                isLexicallyStrict, hasFunctionNameEnvironment, isConstructorFunction, planSeed)
        };

        var capturedPrivateScopes = context.CapturePrivateNameScopes();
        switch (callable)
        {
            case SyncFunctionInvoker typed when context.CurrentPrivateNameScope is not null &&
                                                typed.PrivateNameScope is null:
                typed.SetPrivateNameScope(context.CurrentPrivateNameScope);
                typed.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                break;
            case SyncFunctionInvoker typed:
                typed.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                break;
            case AsyncGeneratorFunctionInvoker asyncGeneratorFactory
                when context.CurrentPrivateNameScope is not null &&
                     asyncGeneratorFactory.PrivateNameScope is null:
                asyncGeneratorFactory.SetPrivateNameScope(context.CurrentPrivateNameScope);
                asyncGeneratorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                break;
            case AsyncGeneratorFunctionInvoker asyncGeneratorFactory:
                asyncGeneratorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                break;
            case GeneratorFunctionBase generatorFactory when context.CurrentPrivateNameScope is not null &&
                                                             generatorFactory.PrivateNameScope is null:
                generatorFactory.SetPrivateNameScope(context.CurrentPrivateNameScope);
                generatorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                break;
            case GeneratorFunctionBase generatorFactory:
                generatorFactory.SetCapturedPrivateNameScopes(capturedPrivateScopes);
                break;
        }

        // Per ES spec 13.3.1.4: If IsAnonymousFunctionDefinition(Initializer) is true and
        // hasNameProperty is false, perform SetFunctionName(value, bindingId).
        if (functionExpression.Name is null &&
            context.CurrentFunctionNameHint is { } inferredName &&
            callable is IFunctionNameTarget nameTarget)
        {
            nameTarget.EnsureHasName(inferredName.Name);
        }

        // Store the function in the functionNameEnvironment's slot 0 for self-reference
        // Also register in dictionary with isImmutableBinding=true so eval'd code can detect immutability
        if (functionNameEnvironment is null)
        {
            return callable;
        }

        // Store in slot if slots are initialized (with scope analysis)
        if (functionNameEnvironment._slots is not null && functionNameEnvironment._slotCount > 0)
        {
            functionNameEnvironment._slots[0].Value = JsValue.FromObjectUnsafe(callable);
        }

        // Register as immutable binding in dictionary for eval compatibility and fallback lookup
        // Per ES spec 9.2.10, function name binding is immutable:
        // - strict mode: assignment throws TypeError
        // - non-strict mode: assignment is silently ignored
        functionNameEnvironment.DefineJsValue(functionExpression.Name!, JsValue.FromObjectUnsafe(callable),
            isLexicalBinding: true, blocksFunctionScopeOverride: true, isImmutableBinding: true);

        return callable;
    }

    private static bool ShouldCreateIrSyncGeneratorInvoker(
        FunctionExpression functionExpression,
        JsEnvironment closureEnvironment,
        FunctionExecutionPlanSeed planSeed)
    {
        // Non-simple parameter lists (defaults, destructuring, rest) are routed through the
        // unified resumable VM whenever it accepts them. The few shapes the resumable route
        // declines — direct eval in the parameter list, parameter-capturing closures, an
        // arguments-object dependency, or root hoisted function declarations — were previously
        // declined here too and then hard-threw in SyncGeneratorInvoker. The IR runner performs
        // full FunctionDeclarationInstantiation (separate parameter environment for direct eval,
        // mapped/unmapped arguments object, eager IteratorBindingInitialization at call time), so
        // routing those declined non-simple bodies to the IR fallback restores correct semantics
        // instead of throwing. Resumable-eligible bodies still fall through to the unified route
        // below (this method returns false for them).

        // Non-simple parameter lists whose shape the resumable route cannot eagerly bind
        // (direct eval in the parameter list, or a parameter-capturing closure in a default
        // initializer) are declined by SyncGeneratorInvoker.TryCreateUnifiedBytecodeGenerator
        // AFTER this routing decision, where they would hard-throw. EvaluateResumable below does
        // not model the parameter-binding shape, so mirror that decline here and route them to
        // the IR fallback, which performs full IteratorBindingInitialization.
        if (!functionExpression.HasOnlySimpleIdentifierParameters() &&
            !ResumableParameterShapeAllowsEagerBinding(functionExpression))
        {
            return true;
        }

        var plan = planSeed.Plan;
        if (plan is null)
        {
            if (planSeed.Failure is not null)
            {
                return false;
            }

            var cache = ((IAstCacheable<ExecutionPlanCache>)functionExpression).GetOrCreateCache();
            if (cache.Plan is null)
            {
                return false;
            }

            plan = cache.Plan;
        }

        if (!TryCollectResumableRootHoistedFunctionDeclarations(
                functionExpression,
                plan,
                allowCapturedActivationSlots: true,
                out var hoistedFunctionDeclarations))
        {
            return true;
        }

        var needsFunctionEnvironmentForDisposal =
            UnifiedBytecodeProductionEligibility.PlanNeedsResumableFunctionEnvironmentForDisposal(plan);
        var needsDirectEvalArgumentsObject =
            HasResumableDirectEvalImplicitArgumentsAccess(functionExpression);
        var needsMaterializedBodyEnvironment =
            UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableBodyEnvironment(plan) ||
            HoistedFunctionDeclarationsNeedMaterializedBodyEnvironment(hoistedFunctionDeclarations) ||
            UnifiedBytecodeProductionEligibility.PlanNeedsMaterializedResumableClassDeclarationEnvironment(plan) ||
            needsFunctionEnvironmentForDisposal ||
            needsDirectEvalArgumentsObject;
        var activation = new UnifiedBytecodeProductionActivationDescriptor(
            IsAsyncLike: false,
            IsGenerator: true,
            HasCapturedOrDynamicActivation: HasResumableCapturedOrDynamicActivationDecline(
                functionExpression,
                closureEnvironment,
                allowDeclarationFreeDirectEval: true),
            HasArgumentsObjectDependency: HasResumableArgumentsObjectDependency(
                functionExpression,
                allowDeclarationFreeDirectEval: true),
            AllowsRootFunctionDeclarationInstructions: !hoistedFunctionDeclarations.IsEmpty,
            AllowsMaterializedBodyEnvironmentFunctionLiterals: needsMaterializedBodyEnvironment,
            AllowsNestedFunctionLiteralLexicalThisOrPrivateNameContext:
            UnifiedBytecodeProductionEligibility.PlanNeedsNestedFunctionLiteralLexicalThisOrPrivateNameContext(plan));
        return !UnifiedBytecodeProductionEligibility.EvaluateResumable(plan, activation).IsEligible;
    }

    internal static IJsCallable CreateFunctionValueFromLiteral(
        FunctionExpression functionExpression,
        JsEnvironment environment,
        EvaluationContext context,
        bool isConstructorFunction,
        FunctionExecutionPlanSeed planSeed) =>
        functionExpression.CreateFunctionValue(environment, context, isConstructorFunction, planSeed: planSeed);

    internal static IJsCallable CreateFunctionValueFromDeclaration(
        FunctionLiteralDescriptor descriptor,
        JsEnvironment environment,
        EvaluationContext context) =>
        descriptor.Function.CreateFunctionValue(
            environment,
            context,
            skipInternalNameBinding: true,
            planSeed: descriptor.PlanSeed);
}

#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Well-known symbol for storing yield resume state in the environment.
    /// Used when yields happen inside StatementInstruction (e.g., in destructuring defaults).
    /// </summary>
    private static readonly Symbol YieldResumeStateKey = Symbol.Intern("__yield_resume_state__");

    private static JsValue HandleIdentifierNotFound(Symbol name, EvaluationContext context)
    {
        var errorObject = StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState);
        context.SetThrow(errorObject);
        return errorObject;
    }

    private static JsValue ResolveThisValue(JsEnvironment environment, EvaluationContext context)
    {
        if (!environment.IsThisInitializationKnownTrue(context))
        {
            throw StandardLibrary.ThrowReferenceError(
                "Must call super constructor in derived class before accessing 'this'",
                context,
                context.RealmState);
        }

        try
        {
            // Check if we're in an arrow function that has a lexical this environment.
            // If so, read `this` from the original owning environment, not the arrow's local copy.
            // This ensures that after super() updates the constructor's `this`, subsequent
            // reads of `this` inside the arrow function see the updated value.
            if (environment.TryFindBindingJsValue(Symbol.LexicalThisEnvironment, true, out _,
                    out var lexicalEnvValue) &&
                lexicalEnvValue.TryGetObject<JsEnvironment>(out var lexicalThisEnv))
            {
                return lexicalThisEnv.GetJsValue(Symbol.This);
            }

            return environment.GetJsValue(Symbol.This);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                       StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
            throw new ThrowSignal(errorObject);
        }
    }

    /// <summary>
    ///     Evaluates an import.meta expression. Returns the import.meta object for the current module.
    /// </summary>
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateImportMeta(JsEnvironment environment, EvaluationContext context)
    {
        // Try to get the import.meta object from the environment
        // If running in module context, this should be set by the module loader
        if (environment.TryGetJsValue(Symbol.ImportMeta, out var importMeta))
        {
            return importMeta;
        }

        // Return a basic import.meta object with a url property
        var metaObject = new JsObject();
        metaObject.RealmState = context.RealmState;
        metaObject.SetPrototype(null);
        // Set a default URL if we can determine it from the environment
        metaObject.SetProperty("url", string.Empty);
        return (JsValue)metaObject;
    }

    private static (IJsEnvironmentAwareCallable? Constructor, IJsPropertyAccessor? Prototype) ResolveSuperclass(this ExpressionNode? extendsExpression, JsEnvironment environment, EvaluationContext context)
    {
        if (extendsExpression is null)
        {
            return (null, null);
        }

        var baseJsValue = EvaluateCachedExpressionProgram(
            extendsExpression,
            environment,
            context,
            "Dynamic class extends expression");
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (baseJsValue.IsNullOrUndefined)
        {
            return (null, null);
        }

        // Only objects can be constructors - extract ObjectValue directly
        var baseValue = baseJsValue.Kind == JsValueKind.Object ? baseJsValue.ObjectValue : null;

        if (!JsOps.IsConstructor(JsValue.FromObjectUnsafe(baseValue)))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value is not a constructor or null", context, context.RealmState));
        }

        // Proxy cannot be subclassed because its prototype is undefined.
        if (baseValue is IJsPropertyAccessor accessorWithMarker &&
            JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(accessorWithMarker), "__proxyHasNoPrototype__",
                out var marker, context) &&
            JsOps.ToBoolean(marker))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState));
        }

        if (baseValue is not (IJsEnvironmentAwareCallable callable and IJsPropertyAccessor))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value is not a constructor or null", context, context.RealmState));
        }

        var hasPrototype = JsOps.TryGetPropertyValue(baseJsValue, "prototype", out var prototypeValue, context);
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (!hasPrototype)
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState));
        }

        // Per ES spec 15.7.14 step 7.c.iii: only null is acceptable, not undefined
        if (prototypeValue.IsNull)
        {
            return (callable, null);
        }

        if (prototypeValue.TryGetObject<IJsPropertyAccessor>(out var prototype))
        {
            return (callable, prototype);
        }

        throw new ThrowSignal(StandardLibrary.CreateTypeError(
            "Class extends value does not have a valid prototype",
            context,
            context.RealmState));
    }

    /// <summary>
    /// Ultra-thin hot path for expression evaluation - designed to be inlined.
    /// Uses explicit if statements instead of switch for minimal IL size.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateIdentifier(this IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        // `arguments` is an implicit binding; its slot isn't present in the analyzer's slot map,
        // so a cached slot hint can incorrectly point to an outer scope (e.g., a `var arguments`).
        // Always resolve it via normal binding lookup to ensure the per-call arguments object wins.
        if (ReferenceEquals(identifier.Name, Symbol.Arguments))
        {
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var argumentsValue))
            {
                return argumentsValue;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        if (!context.AllowIdentifierCache)
        {
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var value))
            {
                return value;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        if (environment.TryReadIdentifierWithSlot(identifier, context, out var slotValue))
        {
            return slotValue;
        }

        // Slow path: identifier not found - create proper error
        // Compiled out when TRACE_IR_EXECUTION not defined
        ExecutionPlanPrinter.TraceLookup(
            context.RealmState.Logger,
            identifier.Name.Name,
            false,
            environment.Depth,
            environment.ScopeId,
            environment.GetHashCode(),
            $"idScope={identifier.ScopeId} slot={identifier.SlotIndex}");
        return HandleIdentifierNotFound(identifier.Name, context);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateExpression(this ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        context.RecordAstEvaluation(expression);
        return expression switch
        {
            // Explicit if statements generate less IL than switch expressions
            LiteralExpression literal => literal.Value,
            IdentifierExpression identifier => identifier.EvaluateIdentifier(environment, context),
            BinaryExpression binary => EvaluateCachedExpressionProgram(
                binary,
                environment,
                context,
                "Dynamic binary expression"),
            AssignmentExpression assignment => assignment.EvaluateAssignment(environment, context),
            UnaryExpression unary => unary.EvaluateUnary(environment, context),
            CallExpression call => call.EvaluateCall(environment, context),
            _ => expression.EvaluateExpressionSlow(environment, context)
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateDynamicExpressionOperand(
        this ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        string failureLabel)
    {
        return expression switch
        {
            AwaitExpression awaitExpression => awaitExpression.EvaluateAwait(environment, context),
            YieldExpression yieldExpression => yieldExpression.EvaluateYield(environment, context),
            _ => EvaluateCachedExpressionProgram(expression, environment, context, failureLabel)
        };
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateAwait(this AwaitExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Async generators execute on the generator IR path via ExecutionPlanRunner.
        // When an await expression runs under that executor, the execution environment
        // carries a back-reference to the active generator instance so we can surface
        // pending promises instead of blocking. In that case the generator instance
        // is responsible for evaluating the awaited expression and managing resume.
        if (environment.TryGetObject<ExecutionPlanRunner>(Symbol.GeneratorInstanceSymbol, out var generator))
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    expression.Expression,
                    out var awaitedProgram,
                    out var failureReason))
            {
                throw new NotSupportedException(
                    $"Async generator await operand could not be lowered to expression bytecode: {failureReason}");
            }

            return generator.EvaluateAwaitInGenerator(
                expression.GetAwaitStateKey(),
                awaitedProgram,
                environment,
                context);
        }

        var awaitedValue = expression.Expression.EvaluateDynamicExpressionOperand(
            environment,
            context,
            "Dynamic await operand");
        if (context.ShouldStopEvaluation)
        {
            return awaitedValue;
        }

        // Always await asynchronously: wrap non-promises with Promise.resolve and drive through scheduler.
        if (!awaitedValue.IsObject || !AwaitScheduler.IsPromiseLike(awaitedValue))
        {
            var promiseCtor = context.RealmState.PromiseConstructor;
            JsObject? wrappedPromise = null;

            if (promiseCtor is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("resolve", out var resolveValue) &&
                resolveValue.TryGetObject<IJsCallable>(out var resolveCallable))
            {
                var resolveResult = resolveCallable.Invoke(
                    new SingleValueArgs(awaitedValue),
                    JsValue.FromObjectUnsafe(promiseCtor));
                if (resolveResult.IsObject)
                {
                    wrappedPromise = resolveResult.AsObject();
                }
            }

            if (wrappedPromise is null)
            {
                // Fallback: create a resolved promise in the current realm.
                var engine = context.RealmState.Engine;
                var promise = engine?.CreateRealmPromise();
                promise?.Resolve(awaitedValue);
                wrappedPromise = promise?.JsObject;
            }

            awaitedValue = wrappedPromise is not null ? new JsValue(wrappedPromise) : awaitedValue;
        }

        var completed = AwaitScheduler.TryAwaitPromiseSync(
            awaitedValue,
            context,
            out var resolvedValue,
            context.DrainAwaitMicrotasks,
            blockUntilSettled: true);

        if (!completed)
        {
            if (!context.IsThrow)
            {
                throw new InvalidOperationException("Legacy await did not settle synchronously.");
            }

            return JsValue.Undefined;
        }

        return resolvedValue;
    }

    private static Symbol GetAwaitStateKey(this AwaitExpression expression)
    {
        return ((IAstCacheable<Symbol>)expression).GetOrCreateCache();
    }

    /// <summary>
    /// Sets the yield resume value in the environment so that the next call to EvaluateYield
    /// with a matching source position will return this value instead of yielding.
    /// </summary>
    internal static void SetYieldResumeValue(JsEnvironment environment, JsValue resumeValue, int yieldSourceStart,
        int yieldSourceEnd)
    {
        var state = new YieldResumeState
        {
            HasResumeValue = true,
            ResumeValue = resumeValue,
            YieldSourceStart = yieldSourceStart,
            YieldSourceEnd = yieldSourceEnd
        };

        if (environment.HasOwnBinding(YieldResumeStateKey))
        {
            environment.AssignJsValue(YieldResumeStateKey, JsValue.FromObjectUnsafe(state));
        }
        else
        {
            environment.DefineJsValue(YieldResumeStateKey, JsValue.FromObjectUnsafe(state), isLexicalBinding: true,
                canDelete: true);
        }
    }

    /// <summary>
    /// State for resuming from a yield that happened during AST evaluation (via StatementInstruction).
    /// </summary>
    internal sealed class YieldResumeState
    {
        /// <summary>
        /// When true, the yield has been resumed and ResumeValue should be returned.
        /// </summary>
        public bool HasResumeValue { get; set; }

        /// <summary>
        /// The value passed to iter.next(value) when resuming.
        /// </summary>
        public JsValue ResumeValue { get; set; }

        /// <summary>
        /// Source position of the yield expression that yielded.
        /// Used to match the correct yield on resume.
        /// </summary>
        public int YieldSourceStart { get; set; }

        public int YieldSourceEnd { get; set; }
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateYield(this YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Most yield expressions should be lowered by GeneratorYieldLowerer and compiled to IR.
        // However, some yields (like those in destructuring default values) cannot be extracted
        // and are evaluated via StatementInstruction wrapping the containing for-of loop.
        // In this case, we signal a yield via the context so the caller can save state.

        if (expression.IsDelegated)
        {
            // yield* is more complex and should be handled by the IR interpreter.
            // If we reach here with yield*, something went wrong.
            throw new InvalidOperationException(
                "Delegated yield (yield*) expression encountered during AST evaluation. " +
                "This should have been lowered to IR by GeneratorYieldLowerer. " +
                $"Source: {expression.Source?.StartPosition}-{expression.Source?.EndPosition}");
        }

        // Check if we're resuming from a previous yield at this position.
        // If so, return the resume value instead of yielding again.
        if (environment.TryGetObject<YieldResumeState>(YieldResumeStateKey, out var resumeState) &&
            resumeState.HasResumeValue &&
            resumeState.YieldSourceStart == (expression.Source?.StartPosition ?? -1) &&
            resumeState.YieldSourceEnd == (expression.Source?.EndPosition ?? -1))
        {
            // Clear the resume state so future yields at this position work correctly
            resumeState.HasResumeValue = false;
            return resumeState.ResumeValue;
        }

        // Evaluate the yield operand if present
        var yieldedValue = JsValue.Undefined;
        if (expression.Expression is not null)
        {
            yieldedValue = expression.Expression.EvaluateDynamicExpressionOperand(
                environment,
                context,
                "Dynamic yield operand");
            if (context.ShouldStopEvaluation)
            {
                return yieldedValue;
            }
        }

        // Signal the yield via the context.
        // Use the source position to identify this yield for resume.
        context.SetYield(yieldedValue, expression.Source?.StartPosition ?? -1);

        // Store the yield position so the IR interpreter can set up resume state
        context.LastYieldSourceStart = expression.Source?.StartPosition ?? -1;
        context.LastYieldSourceEnd = expression.Source?.EndPosition ?? -1;

        // Return undefined; the actual resume value will be provided when the generator continues.
        // The caller (e.g., BindArrayPattern) will check context.IsYield and save state.
        return JsValue.Undefined;
    }

    /// <summary>
    /// Slow path for less common expression types. Marked NoInlining to keep hot path small.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateExpressionSlow(this ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Second tier of common expressions
        switch (expression)
        {
            case UnaryExpression unary:
                return unary.EvaluateUnary(environment, context);
            case AssignmentExpression assignment:
                return assignment.EvaluateAssignment(environment, context);
            case MemberExpression member:
                return member.EvaluateMember(environment, context);
            case CallExpression call:
                return call.EvaluateCall(environment, context);
        }

        // Slowest path - set source reference and optionally trace
        context.SourceReference = expression.Source;

        return expression switch
        {
            RegexLiteralExpression regex => new JsValue(RegExpHelper.CreateRegExpLiteral(
                regex.Pattern,
                regex.Flags,
                context.RealmState)),
            ConditionalExpression conditional => EvaluateConditionalExpression(
                conditional,
                environment,
                context),
            FunctionExpression functionExpression => JsValue.FromObjectUnsafe(
                functionExpression.CreateFunctionValue(environment, context)),
            DestructuringAssignmentExpression destructuringAssignment => EvaluateCachedExpressionProgram(
                destructuringAssignment,
                environment,
                context,
                "Dynamic destructuring assignment"),
            PropertyAssignmentExpression propertyAssignment => EvaluateCachedExpressionProgram(
                propertyAssignment,
                environment,
                context,
                "Dynamic property assignment"),
            IndexAssignmentExpression indexAssignment => EvaluateCachedExpressionProgram(
                indexAssignment,
                environment,
                context,
                "Dynamic index assignment"),
            SequenceExpression sequence => EvaluateCachedExpressionProgram(
                sequence,
                environment,
                context,
                "Dynamic sequence expression"),
            NewExpression newExpression => EvaluateCachedExpressionProgram(
                newExpression,
                environment,
                context,
                "Dynamic new expression"),
            NewTargetExpression => environment.TryGetJsValue(Symbol.NewTarget, out var newTarget)
                ? newTarget
                : JsValue.Undefined,
            ImportMetaExpression => EvaluateImportMeta(environment, context),
            ArrayExpression array => EvaluateArrayExpression(
                array,
                environment,
                context),
            ObjectExpression obj => EvaluateObjectExpression(
                obj,
                environment,
                context),
            ClassExpression classExpression => EvaluateClassExpressionValue(
                classExpression,
                environment,
                context),
            DecoratorExpression => throw new NotSupportedException("Decorators are not supported."),
            TemplateLiteralExpression template => EvaluateTemplateLiteralExpression(
                template,
                environment,
                context),
            TaggedTemplateExpression taggedTemplate => taggedTemplate.EvaluateTaggedTemplate(environment, context),
            AwaitExpression awaitExpression => awaitExpression.EvaluateAwait(environment, context),
            YieldExpression yieldExpression => yieldExpression.EvaluateYield(environment, context),
            ThisExpression => ResolveThisValue(environment, context),
            SuperExpression => throw new InvalidOperationException(
                $"Super is not available in this context.{context.GetSourceInfo(expression.Source)}"),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
        };
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateConditionalExpression(
        ConditionalExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var test = EvaluateCachedExpressionProgram(
            expression.Test,
            environment,
            context,
            "Dynamic conditional test");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return test.IsTruthy
            ? EvaluateCachedExpressionProgram(
                expression.Consequent,
                environment,
                context,
                "Dynamic conditional consequent")
            : EvaluateCachedExpressionProgram(
                expression.Alternate,
                environment,
                context,
                "Dynamic conditional alternate");
    }

    private static string GetTypeofStringValue(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.BigInt => "bigint",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.Object => GetTypeofStringForObject(value.ObjectValue),
            _ => "undefined"
        };
    }

    private static string GetTypeofStringForObject(object? obj)
    {
        if (obj is IIsHtmlDda)
        {
            return "undefined";
        }

        if (obj is JsProxy proxy)
        {
            return proxy.IsCallableTarget() ? "function" : "object";
        }

        return obj is IJsCallable ? "function" : "object";
    }

    private static JsValue BitwiseNotValue(in JsValue operand, EvaluationContext context)
    {
        return BitwiseNotJsValue(in operand, context);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateClassExpressionValue(
        ClassExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var inferredName = expression.Name ?? context.CurrentFunctionNameHint;
        return expression.Definition.CreateClassValue(environment, context, inferredName);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateArrayExpression(
        ArrayExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var array = new JsArray(context.RealmState);
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValueJs = EvaluateCachedExpressionProgram(
                    element.Expression!,
                    environment,
                    context,
                    "Dynamic array spread expression");
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValueJs, context))
                {
                    array.Push(item);
                }

                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                continue;
            }

            if (element.Expression is null)
            {
                array.PushHole();
            }
            else
            {
                array.Push(EvaluateCachedExpressionProgram(
                    element.Expression,
                    environment,
                    context,
                    "Dynamic array element expression"));
            }

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        return JsValue.FromJsArray(array);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateObjectExpression(
        ObjectExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
        obj.RealmState = context.RealmState;
        if (context.RealmState.ObjectPrototype is { } objectProto)
        {
            obj.SetPrototype(objectProto);
        }

        foreach (var member in expression.Members)
        {
            switch (member.Kind)
            {
                case ObjectMemberKind.Property:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var valueJs = member.Value is null
                            ? JsValue.Undefined
                            : EvaluateCachedExpressionProgram(
                                member.Value,
                                environment,
                                context,
                                "Dynamic object property value");

                        if (!member.IsComputed &&
                            string.Equals(name, "__proto__", StringComparison.Ordinal) &&
                            member.Parameter is null)
                        {
                            if (valueJs.IsNull)
                            {
                                obj.SetPrototype(null);
                            }
                            else if (valueJs.TryGetObject<IJsPropertyAccessor>(out var protoAccessor))
                            {
                                obj.SetPrototype(protoAccessor);
                            }

                            break;
                        }

                        if (valueJs.ObjectValue is IFunctionNameTarget nameTarget &&
                            member.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
                        {
                            nameTarget.EnsureHasName(BuildFunctionNameDisplay(name));
                        }

                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                JsValue = valueJs,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Method:
                    {
                        var callable = member.Function!.CreateFunctionValue(environment, context, false);
                        if (callable is SyncFunctionInvoker typed)
                        {
                            typed.SetHomeObject(obj);
                            typed.DisableConstruction();
                        }
                        else if (callable is SyncGeneratorInvoker generatorFactory)
                        {
                            generatorFactory.SetHomeObject(obj);
                            generatorFactory.DisableConstruction();
                        }
                        else if (callable is AsyncGeneratorFunctionInvoker asyncGeneratorFactory)
                        {
                            asyncGeneratorFactory.SetHomeObject(obj);
                            asyncGeneratorFactory.DisableConstruction();
                        }

                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (callable is IFunctionNameTarget nameTarget)
                        {
                            nameTarget.EnsureHasName(BuildFunctionNameDisplay(name));
                        }

                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                Value = callable,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Getter:
                    {
                        var getter = new SyncFunctionInvoker(
                            member.Function!,
                            environment,
                            context.RealmState,
                            context.CurrentScope.IsStrict,
                            isConstructorFunction: false);
                        getter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        getter.EnsureHasName($"get {BuildFunctionNameDisplay(name)}");

                        obj.DefineAccessorProperty(name, getter, null);
                        break;
                    }
                case ObjectMemberKind.Setter:
                    {
                        var setter = new SyncFunctionInvoker(
                            member.Function!,
                            environment,
                            context.RealmState,
                            context.CurrentScope.IsStrict,
                            isConstructorFunction: false);
                        setter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        setter.EnsureHasName($"set {BuildFunctionNameDisplay(name)}");

                        obj.DefineAccessorProperty(name, null, setter);
                        break;
                    }
                case ObjectMemberKind.Field:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var valueJs = member.Value is null
                            ? JsValue.Undefined
                            : EvaluateCachedExpressionProgram(
                                member.Value,
                                environment,
                                context,
                                "Dynamic object field value");
                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                JsValue = valueJs,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Spread:
                    {
                        var spreadValueJs = EvaluateCachedExpressionProgram(
                            member.Value!,
                            environment,
                            context,
                            "Dynamic object spread value");
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (spreadValueJs.IsNullOrUndefined)
                        {
                            break;
                        }

                        if (spreadValueJs.ObjectValue is IIsHtmlDda)
                        {
                            break;
                        }

                        if (spreadValueJs.ObjectValue is IDictionary<string, object?> dictionary and not JsObject)
                        {
                            foreach (var kvp in dictionary)
                            {
                                obj.DefineProperty(kvp.Key,
                                    new PropertyDescriptor
                                    {
                                        Value = kvp.Value,
                                        Writable = true,
                                        Enumerable = true,
                                        Configurable = true
                                    });
                            }

                            break;
                        }

                        var accessor = spreadValueJs.ObjectValue is IJsPropertyAccessor propertyAccessor
                            ? propertyAccessor
                            : ToObjectForDestructuringJsValue(spreadValueJs, context);

                        foreach (var key in accessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
                        {
                            var desc = accessor.GetOwnPropertyDescriptor(key);
                            if (desc is not { Enumerable: true })
                            {
                                continue;
                            }

                            var spreadPropertyValue = accessor.TryGetProperty(key, out var val)
                                ? val
                                : JsValue.Undefined;
                            obj.DefineProperty(key,
                                new PropertyDescriptor
                                {
                                    JsValue = spreadPropertyValue,
                                    Writable = true,
                                    Enumerable = true,
                                    Configurable = true
                                });
                        }

                        break;
                    }
            }
        }

        return new JsValue(obj);
    }

    private static string BuildFunctionNameDisplay(this string propertyName)
    {
        if (JsSymbol.TryGetByInternalKey(propertyName, out var symbol))
        {
            return symbol!.Description is null ? string.Empty : $"[{symbol.Description}]";
        }

        return propertyName;
    }

    private static string ResolveObjectMemberName(
        ObjectMember member,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode keyExpression)
            {
                throw new InvalidOperationException("Computed property name must be an expression.");
            }

            var keyValue = EvaluateCachedExpressionProgram(
                keyExpression,
                environment,
                context,
                "Dynamic object member name");
            if (context.ShouldStopEvaluation)
            {
                return string.Empty;
            }

            var propertyName = JsOps.GetRequiredPropertyName(keyValue, context);
            return context.ShouldStopEvaluation ? string.Empty : propertyName;
        }

        if (context.ShouldStopEvaluation)
        {
            return string.Empty;
        }

        var propertyNameFromKey = JsOps.GetRequiredPropertyName(JsValue.FromObjectUnsafe(member.Key), context);
        return context.ShouldStopEvaluation ? string.Empty : propertyNameFromKey;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateUnary(
        this UnaryExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression is
            {
                Operator: UnaryOperator.Delete,
                Operand: MemberExpression { IsOptional: true }
            })
        {
            return expression.Operand.EvaluateDelete(environment, context) ? JsValue.True : JsValue.False;
        }

        return EvaluateCachedExpressionProgram(
            expression,
            environment,
            context,
            "Dynamic unary expression");
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateTemplateLiteralExpression(
        TemplateLiteralExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
            {
                builder.Append(part.Text);
                continue;
            }

            if (part.Expression is null)
            {
                continue;
            }

            var valueJs = EvaluateCachedExpressionProgram(
                part.Expression,
                environment,
                context,
                "Dynamic template literal expression");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            builder.Append(valueJs.ToJsString());
        }

        return new JsValue(builder.ToString());
    }

    private static string DescribeCallee(this ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierExpression id => id.Name.Name,
            MemberExpression member => $"{member.Target.DescribeCallee()}.{member.Property.DescribeMemberName()}",
            CallExpression call => $"{call.Callee.DescribeCallee()}(...)",
            _ => expression.GetType().Name
        };
    }

    private static bool IsAnonymousFunctionDefinition(this ExpressionNode expression)
    {
        return expression.IsAnonymousFunctionDefinitionNode();
    }

    private static bool IsAnonymousFunctionDefinitionNode(this ExpressionNode node)
    {
        // Per ES spec, sequence expressions (comma operator) do not qualify for name inference
        // e.g., `const x = (0, function() {})` should not infer name
        if (node is SequenceExpression)
        {
            return false;
        }

        return node switch
        {
            FunctionExpression func => func.Name is null,
            ClassExpression classExpression => classExpression.Name is null,
            _ => false
        };
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static (JsValue Callee, JsValue thisValue, bool SkippedOptional) EvaluateCallTarget(this ExpressionNode callee, JsEnvironment environment,
        EvaluationContext context)
    {
        switch (callee)
        {
            case SuperExpression superExpression:
                {
                    var logger = environment.RealmState?.Logger;
                    logger?.LogInformation(
                        "Super call target env={Env} hasThisInit={HasThisInit} hasSuper={HasSuper}",
                        environment.GetHashCode(),
                        environment.HasBinding(Symbol.ThisInitialized),
                        environment.HasBinding(Symbol.Super));
                    var binding = environment.ExpectSuperBinding(context);

                    // Per ES spec 12.3.5.1 SuperCall, the super constructor should be looked up
                    // dynamically via GetSuperConstructor() which gets activeFunction.[[Prototype]].
                    // For a constructor, the active function is available via NewTarget when it's
                    // a constructor being invoked via 'new'.
                    var dynamicSuperConstructor = environment.ResolveSuperConstructorForCall(binding);
                    if (environment.TryGetObject<IJsObjectLike>(Symbol.NewTarget, out var activeFunction))
                    {
                        logger?.LogInformation(
                            "Super call: dynamic lookup newTargetType={NewTargetType} protoType={ProtoType}",
                            activeFunction.GetType().Name,
                            dynamicSuperConstructor?.GetType().Name ?? "null");
                    }

                    if (dynamicSuperConstructor is null)
                    {
                        throw new InvalidOperationException(
                            $"Super constructor is not available in this context.{context.GetSourceInfo(superExpression.Source)}");
                    }

                    var superThis = binding.ThisValue.IsUninitialized
                        ? JsValue.Undefined
                        : binding.ThisValue;

                    return (JsValue.FromObjectUnsafe(dynamicSuperConstructor), superThis, false);
                }
            case MemberExpression { Target: SuperExpression } member:
                {
                    var (memberValue, binding) = member.ResolveSuperMember(environment, context);
                    return context.ShouldStopEvaluation
                        ? (JsValue.Undefined, thisValue: binding.ThisValue, true)
                        : (memberValue, thisValue: binding.ThisValue, false);
                }
            case MemberExpression member:
                {
                    var targetJs = EvaluateCachedExpressionProgram(
                        member.Target,
                        environment,
                        context,
                        "Dynamic call target member target");
                    if (context.ShouldStopEvaluation
                        || (member.IsOptional && targetJs.IsNullOrUndefined)
                        || (targetJs.IsNullOrUndefined && HasOptionalChaining(member.Target)))
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    if (targetJs.IsNullOrUndefined)
                    {
                        var error = StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState);
                        context.SetThrow(error);
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    string propertyName;
                    if (member.IsComputed)
                    {
                        var propertyJs = EvaluateCachedExpressionProgram(
                            member.Property,
                            environment,
                            context,
                            "Dynamic call target computed property");
                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }

                        propertyName = JsOps.GetRequiredPropertyName(propertyJs, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }
                    }
                    else
                    {
                        propertyName = member.Property switch
                        {
                            IdentifierExpression id => id.Name.Name,
                            LiteralExpression { Value.IsString: true } lit => lit.Value.AsString(),
                            _ => JsOps.GetRequiredPropertyName(
                                EvaluateCachedExpressionProgram(
                                    member.Property,
                                    environment,
                                    context,
                                    "Dynamic call target property"),
                                context)
                        };
                    }

                    if (member.IsComputed || !propertyName.IsPrivateName())
                    {
                        if (targetJs.TryGetObject<IJsPropertyAccessor>(out var accessor))
                        {
                            try
                            {
                                if (accessor.TryGetProperty(propertyName, targetJs, out var directJsValue))
                                {
                                    return (directJsValue, targetJs, false);
                                }
                            }
                            catch (ThrowSignal signal)
                            {
                                context.SetThrow(signal.ThrownValue);
                                return (JsValue.Undefined, JsValue.Undefined, true);
                            }
                        }

                        if (JsOps.TryGetPropertyValue(targetJs, propertyName, out var directValue, context))
                        {
                            if (context.ShouldStopEvaluation)
                            {
                                return (JsValue.Undefined, JsValue.Undefined, true);
                            }

                            return (directValue, targetJs, false);
                        }

                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }

                        return (JsValue.Undefined, targetJs, false);
                    }

                    var handle = PropertyHandle.Resolve(
                        targetJs,
                        propertyName,
                        context,
                        context.CurrentScope.IsStrict,
                        !member.IsComputed);
                    var value = handle.GetJsValue();
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    return (value, targetJs, false);
                }
            case IdentifierExpression identifier
                when environment.TryResolveWithBinding(identifier.Name, context, out var withBinding):
                try
                {
                    var withValue = JsEnvironment.GetWithBindingValueJsValue(withBinding);
                    return (withValue, JsValue.FromObjectUnsafe(withBinding.BindingObject), false);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                               StringComparison.Ordinal))
                {
                    // Convert to JavaScript ReferenceError so it can be caught by JavaScript try-catch
                    var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
                    throw new ThrowSignal(errorObject);
                }

            // Fast path: use slot-based lookup when available
            case IdentifierExpression identifier:
                {
                    if (identifier is { SlotIndex: >= 0, ScopeId: >= 0 })
                    {
                        if (environment.TryReadIdentifierWithSlot(
                                identifier.Name,
                                identifier.ScopeId,
                                identifier.SlotIndex,
                                context,
                                out var slotCallee))
                        {
                            return (slotCallee, JsValue.Undefined, false);
                        }
                    }

                    // Fallback: dictionary-based lookup
                    var reference = environment.ResolveIdentifierAssignmentReference(identifier.Name, context);
                    if (reference.IsUnresolvable)
                    {
                        var error = StandardLibrary.CreateReferenceError(
                            $"{identifier.Name.Name} is not defined",
                            context,
                            context.RealmState);
                        context.SetThrow(error);
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    var calleeValue = AssignmentReferenceResolver.ReadIdentifierValue(reference.GetJsValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    return (calleeValue, JsValue.Undefined, false);
                }
            default:
                {
                    var directCallee = EvaluateCachedExpressionProgram(
                        callee,
                        environment,
                        context,
                        "Dynamic direct callee");
                    return (directCallee, JsValue.Undefined, false);
                }
        }
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static bool EvaluateDelete(this ExpressionNode operand, JsEnvironment environment, EvaluationContext context)
    {
        switch (operand)
        {
            case MemberExpression member:
                {
                    if (member.Target is SuperExpression)
                    {
                        throw StandardLibrary.ThrowReferenceError(
                            "Cannot delete property on super reference",
                            context,
                            context.RealmState);
                    }

                    var targetJs = EvaluateCachedExpressionProgram(
                        member.Target,
                        environment,
                        context,
                        "Dynamic delete member target");
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var propertyValueJs = EvaluateCachedExpressionProgram(
                        member.Property,
                        environment,
                        context,
                        "Dynamic delete member property");
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var handle = PropertyHandle.Resolve(
                        targetJs,
                        propertyValueJs,
                        context,
                        context.CurrentScope.IsStrict,
                        !member.IsComputed);
                    return handle.Delete();
                }
            case IdentifierExpression when context.CurrentScope.IsStrict:
                throw StandardLibrary.ThrowSyntaxError(
                    "Delete of an unqualified identifier is not allowed in strict mode.",
                    context,
                    context.RealmState);
            case IdentifierExpression identifier:
                {
                    var outcome = environment.DeleteBinding(identifier.Name);
                    return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
                }
            default:
                _ = operand.EvaluateDynamicExpressionOperand(
                    environment,
                    context,
                    "Dynamic delete operand");
                return true;
        }
    }

    private static string DescribeMemberName(this ExpressionNode property)
    {
        return property switch
        {
            LiteralExpression { Value.IsString: true } lit => lit.Value.AsString(),
            IdentifierExpression id => id.Name.Name,
            _ => property.GetType().Name
        };
    }
}

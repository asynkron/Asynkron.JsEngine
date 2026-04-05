#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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

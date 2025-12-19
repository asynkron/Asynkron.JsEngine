using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

internal static class AssignmentReferenceResolver
{
    /// <summary>
    /// Fastest path - resolve a Symbol directly without any expression object allocation.
    /// Use this when you already have the Symbol (e.g., from AssignmentExpression.Target).
    /// </summary>
    public static AssignmentReference ResolveIdentifierDirect(
        Symbol name,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var isStrictTarget = context.CurrentScope.IsStrict &&
                             (string.Equals(name.Name, "eval", StringComparison.Ordinal) ||
                              string.Equals(name.Name, "arguments", StringComparison.Ordinal));

        if (environment.TryResolveWithBinding(name, context, out var withBinding))
        {
            return AssignmentReference.ForWithBinding(
                withBinding,
                environment,
                name,
                context,
                isStrictTarget);
        }

        var reference = environment.ResolveIdentifierAssignmentReference(name, context);
        if (!isStrictTarget)
        {
            return reference;
        }

        // Wrap in delegate for strict restricted names (eval/arguments)
        return AssignmentReference.ForDelegate(
            reference.GetValue,
            _ => throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateSyntaxError(
                "Assignment to eval or arguments is not allowed in strict mode.", context,
                context.RealmState))));
    }

    /// <summary>
    /// Fast path for resolving simple identifier expressions without allocating a delegate.
    /// Use this when you know the expression is an IdentifierExpression or can be unwrapped to one.
    /// </summary>
    public static AssignmentReference ResolveIdentifierFast(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        // Unwrap unary ++/-- expressions to get the underlying identifier
        while (expression is UnaryExpression { Operator: UnaryOperator.Increment or UnaryOperator.Decrement } unary)
        {
            expression = unary.Operand;
        }

        if (expression is IdentifierExpression identifier)
        {
            return ResolveIdentifierDirect(identifier.Name, environment, context);
        }

        // For non-identifier expressions, throw - caller should use full Resolve method
        throw new InvalidOperationException(
            $"ResolveIdentifierFast only supports identifier expressions, got {expression.GetType().Name}");
    }

    public static AssignmentReference Resolve(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, JsValue> evaluateExpression)
    {
        return Resolve(expression, environment, context, evaluateExpression, deferPropertyKeyConversion: false);
    }

    public static AssignmentReference ResolveForDestructuring(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, JsValue> evaluateExpression)
    {
        return Resolve(expression, environment, context, evaluateExpression, deferPropertyKeyConversion: true);
    }

    private static AssignmentReference Resolve(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, JsValue> evaluateExpression,
        bool deferPropertyKeyConversion)
    {
        return expression switch
        {
            IdentifierExpression identifier => ResolveIdentifier(identifier, environment, context),
            MemberExpression member => ResolveMember(member, environment, context, evaluateExpression,
                deferPropertyKeyConversion),
            UnaryExpression { Operator: UnaryOperator.Increment or UnaryOperator.Decrement } unary =>
                Resolve(unary.Operand, environment, context, evaluateExpression),
            _ => throw new NotSupportedException("Unsupported assignment target.")
        };
    }

    private static AssignmentReference ResolveIdentifier(IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        // Delegate to the direct version to avoid code duplication
        return ResolveIdentifierDirect(identifier.Name, environment, context);
    }

    private static AssignmentReference ResolveMember(
        MemberExpression member,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, JsValue> evaluateExpression,
        bool deferPropertyKeyConversion)
    {
        // Member access uses delegate fallback for now (complex cases)
        // This can be optimized later with a dedicated MemberReference struct

        if (member.Target is SuperExpression)
        {
            if (!context.IsThisInitialized)
            {
                throw StandardLibrary.ThrowReferenceError(
                    "Super is not available in this context.",
                    context,
                    context.RealmState);
            }

            var superPropertyValue = evaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return AssignmentReference.ForDelegate(() => Symbol.Undefined, _ => { });
            }

            var binding = environment.ExpectSuperBinding(context);
            string? propertyNameCache = null;

            return AssignmentReference.ForDelegate(
                () =>
                {
                    if (binding.Prototype is null)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "Cannot read properties of null (reading from super)",
                            context,
                            context.RealmState);
                    }

                    var propertyName = GetPropertyName();
                    return binding.TryGetProperty(propertyName, out var value)
                        ? value.ToObject()
                        : Symbol.Undefined;
                },
                newValue =>
                {
                    if (!binding.IsThisInitialized)
                    {
                        throw environment.CreateSuperReferenceError(context, null);
                    }

                    if (binding.Prototype is null)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "Cannot assign to super property when prototype is null or undefined.",
                            context,
                            context.RealmState);
                    }

                    var propertyName = GetPropertyName();
                    if (!binding.TrySetProperty(propertyName, JsValue.FromObjectUnsafe(newValue), out _) &&
                        context.CurrentScope.IsStrict)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot assign to read only property '{propertyName}' of object",
                            context,
                            context.RealmState);
                    }
                });

            string GetPropertyName()
            {
                propertyNameCache ??= JsOps.GetRequiredPropertyName(superPropertyValue, context);
                return propertyNameCache;
            }
        }

        var target = evaluateExpression(member.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return AssignmentReference.ForDelegate(() => Symbol.Undefined, _ => { });
        }

        var propertyValue = evaluateExpression(member.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return AssignmentReference.ForDelegate(() => Symbol.Undefined, _ => { });
        }

        if (target.IsNullish)
        {
            throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                context.RealmState);
        }

        if (deferPropertyKeyConversion)
        {
            string? propertyNameCache = null;

            return AssignmentReference.ForDelegate(
                () =>
                {
                    var handle = GetHandle();
                    return handle.GetValue();
                },
                newValue =>
                {
                    var handle = GetHandle();
                    handle.SetValue(newValue);
                });

            string GetPropertyName()
            {
                propertyNameCache ??= JsOps.GetRequiredPropertyName(propertyValue, context);
                return propertyNameCache;
            }

            TypedAstEvaluator.PropertyHandle GetHandle()
            {
                var propertyName = GetPropertyName();
                return TypedAstEvaluator.PropertyHandle.Resolve(
                    target.ToObject(),
                    propertyName,
                    context,
                    context.CurrentScope.IsStrict,
                    allowPrivate: !member.IsComputed);
            }
        }

        if (target.ObjectValue is TypedArrayBase typedArray &&
            JsOps.TryResolveArrayIndex(propertyValue.ToObject(), out var typedIndex, context))
        {
            return AssignmentReference.ForDelegate(
                () => typedIndex >= 0 && typedIndex < typedArray.Length
                    ? typedArray.GetElement(typedIndex)
                    : Symbol.Undefined,
                newValue =>
                {
                    if (typedIndex >= 0 && typedIndex < typedArray.Length)
                    {
                        typedArray.SetElement(typedIndex, JsOps.ToNumber(newValue));
                    }
                });
        }

        var handle = TypedAstEvaluator.PropertyHandle.Resolve(
            target.ToObject(),
            propertyValue,
            context,
            context.CurrentScope.IsStrict,
            allowPrivate: !member.IsComputed);
        return AssignmentReference.ForDelegate(
            () => handle.GetValue(),
            newValue => handle.SetValue(newValue));
    }

    internal static JsValue ReadIdentifierValue(Func<JsValue> getter, EvaluationContext context)
    {
        try
        {
            return getter();
        }
        catch (InvalidOperationException ex) when (IsReferenceError(ex))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
            context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
            return JsValue.Undefined;
        }
    }

    private static bool IsReferenceError(Exception ex)
    {
        return ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal);
    }

    internal static void AssignObjectProperty(
        JsObject target,
        string propertyName,
        JsValue value,
        bool isStrict,
        EvaluationContext? context = null,
        RealmState? realmState = null,
        object? receiver = null)
    {
        receiver ??= target;
        var receiverValue = JsValue.FromObjectUnsafe(receiver);
        realmState ??= context?.RealmState ?? target.RealmState;

        if (propertyName.IsPrivateSlotName())
        {
            target.SetProperty(propertyName, value, receiverValue);
            return;
        }

        var ownDescriptor = target.GetOwnPropertyDescriptor(propertyName);
        if (ownDescriptor is not null)
        {
            if (ownDescriptor.IsAccessorDescriptor)
            {
                if (ownDescriptor.Set is null)
                {
                    if (isStrict)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot set property '{propertyName}' that has only a getter.", context, realmState);
                    }

                    return;
                }

                TypedAstEvaluator.InvokeCallable(ownDescriptor.Set, [value], receiverValue, context);
                return;
            }

            if (!ownDescriptor.Writable)
            {
                if (isStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot assign to read only property '{propertyName}'.", context, realmState);
                }

                return;
            }

            target.SetProperty(propertyName, value, receiverValue);
            return;
        }

        var inheritedSetter = target.GetSetter(propertyName);
        if (inheritedSetter is not null)
        {
            TypedAstEvaluator.InvokeCallable(inheritedSetter, [value], receiverValue, context);
            return;
        }

        var prototypeAccessor = target.PrototypeAccessor ?? target.Prototype;
        while (prototypeAccessor is not null)
        {
            if (prototypeAccessor is JsObject protoObj)
            {
                var inheritedDescriptor = protoObj.GetOwnPropertyDescriptor(propertyName);
                if (inheritedDescriptor is not null)
                {
                    if (inheritedDescriptor.IsAccessorDescriptor)
                    {
                        if (inheritedDescriptor.Set is null)
                        {
                            if (isStrict)
                            {
                                throw StandardLibrary.ThrowTypeError(
                                    $"Cannot set property '{propertyName}' that has only a getter.",
                                    context,
                                    realmState);
                            }

                            return;
                        }

                        TypedAstEvaluator.InvokeCallable(inheritedDescriptor.Set, [value], receiverValue, context);
                        return;
                    }

                    if (!inheritedDescriptor.Writable)
                    {
                        if (isStrict)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                $"Cannot assign to read only property '{propertyName}'.",
                                context,
                                realmState);
                        }

                        return;
                    }

                    target.DefineProperty(propertyName, new PropertyDescriptor
                    {
                        JsValue = value,
                        Writable = true,
                        Enumerable = inheritedDescriptor.Enumerable,
                        Configurable = inheritedDescriptor.Configurable,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = inheritedDescriptor.HasEnumerable,
                        HasConfigurable = inheritedDescriptor.HasConfigurable
                    });
                    return;
                }

                prototypeAccessor = protoObj.PrototypeAccessor ?? protoObj.Prototype;
                continue;
            }

            if (prototypeAccessor is IJsObjectLike objectLike)
            {
                var inheritedDescriptor = objectLike.GetOwnPropertyDescriptor(propertyName);
                if (inheritedDescriptor is not null)
                {
                    if (inheritedDescriptor.IsAccessorDescriptor)
                    {
                        if (inheritedDescriptor.Set is null)
                        {
                            if (isStrict)
                            {
                                throw StandardLibrary.ThrowTypeError(
                                    $"Cannot set property '{propertyName}' that has only a getter.",
                                    context,
                                    realmState);
                            }

                            return;
                        }

                        TypedAstEvaluator.InvokeCallable(inheritedDescriptor.Set, [value], receiverValue, context);
                        return;
                    }

                    if (!inheritedDescriptor.Writable)
                    {
                        if (isStrict)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                $"Cannot assign to read only property '{propertyName}'.",
                                context,
                                realmState);
                        }

                        return;
                    }

                    target.DefineProperty(propertyName, new PropertyDescriptor
                    {
                        JsValue = value,
                        Writable = true,
                        Enumerable = inheritedDescriptor.Enumerable,
                        Configurable = inheritedDescriptor.Configurable,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = inheritedDescriptor.HasEnumerable,
                        HasConfigurable = inheritedDescriptor.HasConfigurable
                    });
                    return;
                }

                prototypeAccessor.SetProperty(propertyName, value, receiverValue);
                return;
            }

            prototypeAccessor.SetProperty(propertyName, value, receiverValue);
            return;
        }

        if (!target.IsExtensible)
        {
            if (isStrict)
            {
                throw StandardLibrary.ThrowTypeError(
                    $"Cannot add property '{propertyName}', object is not extensible.", context, realmState);
            }

            return;
        }

        target.SetProperty(propertyName, value, receiverValue);
    }
}

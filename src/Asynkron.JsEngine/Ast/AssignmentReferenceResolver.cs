#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

internal static class AssignmentReferenceResolver
{
    /// <summary>
    /// Handles inherited property descriptor logic during property assignment.
    /// Returns true if the property was handled (caller should return), false otherwise.
    /// </summary>
    private static bool TryHandleInheritedDescriptor(
        PropertyDescriptor inheritedDescriptor,
        string propertyName,
        JsValue value,
        JsValue receiverValue,
        JsObject target,
        bool isStrict,
        EvaluationContext? context,
        RealmState? realmState)
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

                return true;
            }

            TypedAstEvaluator.InvokeCallableJsValue(inheritedDescriptor.Set, [value], receiverValue, context);
            return true;
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

            return true;
        }

        target.DefineProperty(propertyName,
            new PropertyDescriptor
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
        return true;
    }

    /// <summary>
    /// Fastest path - resolve a Symbol directly without any expression object allocation.
    /// Use this when you already have the Symbol (e.g., from AssignmentExpression.Target).
    /// </summary>
    public static AssignmentReference ResolveIdentifierDirect(
        Symbol name,
        JsEnvironment environment,
        EvaluationContext context)
    {
        // Use reference equality since Symbols are interned - much faster than string comparison
        var isStrictTarget = context.CurrentScope.IsStrict &&
                             (ReferenceEquals(name, Symbol.Eval) || ReferenceEquals(name, Symbol.Arguments));

        // Fast path: skip with-binding check when AllowIdentifierCache is true (no with/eval in scope)
        if (!context.AllowIdentifierCache && environment.TryResolveWithBinding(name, context, out var withBinding))
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

        // For strict restricted names (eval/arguments), use a specialized reference kind
        // that throws on write. This avoids closure allocation.
        return AssignmentReference.ForStrictRestrictedName(reference, name, context);
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
        return Resolve(expression, environment, context, evaluateExpression, false);
    }

    public static AssignmentReference ResolveForDestructuring(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, JsValue> evaluateExpression)
    {
        return Resolve(expression, environment, context, evaluateExpression, true);
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
            if (!environment.IsThisInitializationKnownTrue(context))
            {
                throw StandardLibrary.ThrowReferenceError(
                    "Super is not available in this context.",
                    context,
                    context.RealmState);
            }

            var superPropertyValue = evaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return AssignmentReference.ForDelegate(static () => JsValue.Undefined, static _ => { });
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
                        ? value
                        : JsValue.Undefined;
                },
                newValue =>
                {
                    if (!binding.IsThisInitialized)
                    {
                        throw environment.CreateSuperReferenceError(context);
                    }

                    if (binding.Prototype is null)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "Cannot assign to super property when prototype is null or undefined.",
                            context,
                            context.RealmState);
                    }

                    var propertyName = GetPropertyName();
                    if (!binding.TrySetProperty(propertyName, newValue, out _) &&
                        environment.IsStrict)
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
            return AssignmentReference.ForDelegate(static () => JsValue.Undefined, static _ => { });
        }

        // For non-computed member access (like obj.prop or obj.#privateField),
        // extract the property name directly rather than evaluating it as an expression.
        // This is critical for private field access where #fieldName is not a variable.
        JsValue propertyValue;
        if (!member.IsComputed)
        {
            var propertyName = member.Property switch
            {
                IdentifierExpression id => id.Name.Name,
                LiteralExpression { Value.IsString: true } lit => lit.Value.AsString()!,
                _ => JsOps.GetRequiredPropertyName(evaluateExpression(member.Property, environment, context), context)
            };
            propertyValue = new JsValue(propertyName);
        }
        else
        {
            propertyValue = evaluateExpression(member.Property, environment, context);
        }

        if (context.ShouldStopEvaluation)
        {
            return AssignmentReference.ForDelegate(static () => JsValue.Undefined, static _ => { });
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
                    return handle.GetJsValue();
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

            PropertyHandle GetHandle()
            {
                var propertyName = GetPropertyName();
                return PropertyHandle.Resolve(
                    target,
                    propertyName,
                    context,
                    context.CurrentScope.IsStrict,
                    !member.IsComputed);
            }
        }

        if (target.ObjectValue is TypedArrayBase typedArray &&
            JsOps.TryResolveArrayIndex(propertyValue, out var typedIndex, context))
        {
            return AssignmentReference.ForDelegate(
                () => typedIndex >= 0 && typedIndex < typedArray.Length
                    ? JsValue.FromDouble(typedArray.GetElement(typedIndex))
                    : JsValue.Undefined,
                newValue =>
                {
                    if (typedIndex >= 0 && typedIndex < typedArray.Length)
                    {
                        typedArray.SetElement(typedIndex, JsOps.ToNumber(newValue, context));
                    }
                });
        }

        var handle = PropertyHandle.Resolve(
            target,
            propertyValue,
            context,
            context.CurrentScope.IsStrict,
            !member.IsComputed);
        return AssignmentReference.ForDelegate(
            handle.GetJsValue,
            handle.SetValue);
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
            context.SetThrow(errorObject);
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

                TypedAstEvaluator.InvokeCallableJsValue(ownDescriptor.Set, [value], receiverValue, context);
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

        // ES2024 10.4.5.5: TypedArray exotic [[Set]] when O !== Receiver.
        // Check BEFORE GetSetter because the TypedArray exotic [[Set]] takes precedence
        // over any setters defined further up the prototype chain (e.g., on TA.prototype).
        {
            var exoticResult = TypedArrayBase.CheckExoticSetInPrototypeChain(
                (IJsPropertyAccessor?)target.PrototypeAccessor ?? target.Prototype, propertyName);
            if (exoticResult == true)
            {
                return; // Invalid index → silently succeed, don't coerce value or create property
            }

            if (exoticResult == false)
            {
                // Valid index, O !== Receiver → OrdinarySet creates data property on receiver.
                // Must use DefineProperty to bypass prototype chain setter lookup.
                if (!target.IsExtensible)
                {
                    if (isStrict)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot add property '{propertyName}', object is not extensible.", context, realmState);
                    }

                    return;
                }

                target.DefineProperty(propertyName,
                    new PropertyDescriptor
                    {
                        JsValue = value,
                        Writable = true,
                        Enumerable = true,
                        Configurable = true
                    });
                return;
            }
        }

        var inheritedSetter = target.GetSetter(propertyName);
        if (inheritedSetter is not null)
        {
            TypedAstEvaluator.InvokeCallableJsValue(inheritedSetter, [value], receiverValue, context);
            return;
        }

        var prototypeAccessor = target.PrototypeAccessor ?? target.Prototype;
        while (prototypeAccessor is not null)
        {
            if (prototypeAccessor is JsProxy proxyPrototype)
            {
                if (!proxyPrototype.TrySetProperty(propertyName, value, receiverValue) && isStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot assign to property '{propertyName}'.",
                        context,
                        realmState);
                }

                return;
            }

            if (prototypeAccessor is JsObject protoObj)
            {
                var inheritedDescriptor = protoObj.GetOwnPropertyDescriptor(propertyName);
                if (inheritedDescriptor is not null &&
                    TryHandleInheritedDescriptor(inheritedDescriptor, propertyName, value, receiverValue,
                        target, isStrict, context, realmState))
                {
                    return;
                }

                prototypeAccessor = protoObj.PrototypeAccessor ?? protoObj.Prototype;
                continue;
            }

            if (prototypeAccessor is IJsObjectLike objectLike)
            {
                var inheritedDescriptor = objectLike.GetOwnPropertyDescriptor(propertyName);
                if (inheritedDescriptor is not null &&
                    TryHandleInheritedDescriptor(inheritedDescriptor, propertyName, value, receiverValue,
                        target, isStrict, context, realmState))
                {
                    return;
                }
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

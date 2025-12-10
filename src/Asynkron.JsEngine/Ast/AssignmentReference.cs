using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

internal readonly record struct AssignmentReference(Func<object?> GetValue, Action<object?> SetValue);

internal static class AssignmentReferenceResolver
{
    public static AssignmentReference Resolve(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression)
    {
        return Resolve(expression, environment, context, evaluateExpression, deferPropertyKeyConversion: false);
    }

    public static AssignmentReference ResolveForDestructuring(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression)
    {
        return Resolve(expression, environment, context, evaluateExpression, deferPropertyKeyConversion: true);
    }

    private static AssignmentReference Resolve(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression,
        bool deferPropertyKeyConversion)
    {
        return expression switch
        {
            IdentifierExpression identifier => ResolveIdentifier(identifier, environment, context),
            MemberExpression member => ResolveMember(member, environment, context, evaluateExpression,
                deferPropertyKeyConversion),
            UnaryExpression { Operator: "++" or "--" } unary =>
                Resolve(unary.Operand, environment, context, evaluateExpression),
            _ => throw new NotSupportedException("Unsupported assignment target.")
        };
    }

    private static AssignmentReference ResolveIdentifier(IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        var isStrictTarget = context.CurrentScope.IsStrict &&
                             (string.Equals(identifier.Name.Name, "eval", StringComparison.Ordinal) ||
                              string.Equals(identifier.Name.Name, "arguments", StringComparison.Ordinal));

        if (environment.TryResolveWithBinding(identifier.Name, context, out var withBinding))
        {
            return new AssignmentReference(
                () => ReadIdentifierValue(() => JsEnvironment.GetWithBindingValue(withBinding), context),
                newValue =>
                {
                    if (isStrictTarget)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateSyntaxError(
                            "Assignment to eval or arguments is not allowed in strict mode.", context,
                            context.RealmState));
                    }

                    if (!JsEnvironment.TrySetWithBindingValue(withBinding, newValue, context.RealmState))
                    {
                        environment.Assign(identifier.Name, newValue);
                    }
                });
        }

        var reference = environment.ResolveIdentifierAssignmentReference(identifier.Name, context);
        if (!isStrictTarget)
        {
            return reference;
        }

        return new AssignmentReference(
            reference.GetValue,
            _ => throw new ThrowSignal(StandardLibrary.CreateSyntaxError(
                "Assignment to eval or arguments is not allowed in strict mode.", context,
                context.RealmState)));
    }

    private static AssignmentReference ResolveMember(
        MemberExpression member,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression,
        bool deferPropertyKeyConversion)
    {
        // According to ES spec 13.3.7.1, for super property access, GetThisBinding must be evaluated
        // BEFORE the property expression to ensure ReferenceError is thrown if this is uninitialized
        // before any side effects occur
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
                return new AssignmentReference(() => Symbol.Undefined, _ => { });
            }

            var binding = TypedAstEvaluator.ExpectSuperBinding(environment, context);
            string? propertyNameCache = null;
            string GetPropertyName()
            {
                propertyNameCache ??= JsOps.GetRequiredPropertyName(superPropertyValue, context);
                return propertyNameCache;
            }

            return new AssignmentReference(
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
                        : Symbol.Undefined;
                },
                newValue =>
                {
                    if (!binding.IsThisInitialized)
                    {
                        throw TypedAstEvaluator.CreateSuperReferenceError(environment, context, null);
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
                        context.CurrentScope.IsStrict)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot assign to read only property '{propertyName}' of object",
                            context,
                            context.RealmState);
                    }
                });
        }

        var target = evaluateExpression(member.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => Symbol.Undefined, _ => { });
        }

        var propertyValue = evaluateExpression(member.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => Symbol.Undefined, _ => { });
        }

        if (target.IsNullish())
        {
            throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                context.RealmState);
        }

        if (deferPropertyKeyConversion)
        {
            string? propertyNameCache = null;
            string GetPropertyName()
            {
                propertyNameCache ??= JsOps.GetRequiredPropertyName(propertyValue, context);
                return propertyNameCache;
            }

            TypedAstEvaluator.PropertyHandle GetHandle()
            {
                var propertyName = GetPropertyName();
                return TypedAstEvaluator.PropertyHandle.Resolve(
                    target,
                    propertyName,
                    context,
                    context.CurrentScope.IsStrict,
                    allowPrivate: !member.IsComputed);
            }

            return new AssignmentReference(
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
        }

        if (target is TypedArrayBase typedArray &&
            JsOps.TryResolveArrayIndex(propertyValue, out var typedIndex, context))
        {
            return new AssignmentReference(
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
            target,
            propertyValue,
            context,
            context.CurrentScope.IsStrict,
            allowPrivate: !member.IsComputed);
        return new AssignmentReference(
            () => handle.GetValue(),
            newValue => handle.SetValue(newValue));
    }

    internal static object? ReadIdentifierValue(Func<object?> getter, EvaluationContext context)
    {
        try
        {
            return getter();
        }
        catch (InvalidOperationException ex) when (IsReferenceError(ex))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
            context.SetThrow(errorObject);
            return Symbol.Undefined;
        }
    }

    private static bool IsReferenceError(Exception ex)
    {
        return ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal);
    }

    internal static void AssignObjectProperty(
        JsObject target,
        string propertyName,
        object? value,
        bool isStrict,
        EvaluationContext? context = null,
        RealmState? realmState = null,
        object? receiver = null)
    {
        receiver ??= target;
        realmState ??= context?.RealmState ?? target.RealmState;

        if (propertyName.IsPrivateSlotName())
        {
            target.SetProperty(propertyName, value, receiver);
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

                TypedAstEvaluator.InvokeCallable(ownDescriptor.Set, [value], receiver, context);
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

            target.SetProperty(propertyName, value, receiver);
            return;
        }

        var inheritedSetter = target.GetSetter(propertyName);
        if (inheritedSetter is not null)
        {
            TypedAstEvaluator.InvokeCallable(inheritedSetter, [value], receiver, context);
            return;
        }

        var prototype = target.Prototype;
        while (prototype is not null)
        {
            var inheritedDescriptor = prototype.GetOwnPropertyDescriptor(propertyName);
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

                    TypedAstEvaluator.InvokeCallable(inheritedDescriptor.Set, [value], receiver, context);
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

                // Writable inherited data property: create/update own data property
                target.DefineProperty(propertyName, new PropertyDescriptor
                {
                    Value = value,
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

            prototype = prototype.Prototype;
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

        target.SetProperty(propertyName, value, receiver);
    }
}

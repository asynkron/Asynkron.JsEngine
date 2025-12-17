using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents a reference to an assignable location (lvalue).
/// Uses a discriminated union pattern to avoid delegate allocations for common identifier cases.
/// </summary>
internal readonly struct AssignmentReference
{
    private enum ReferenceKind : byte
    {
        DeclarativeBinding,   // Most common - cached identifier binding
        GlobalBinding,        // Global object property
        WithBinding,          // With statement binding
        Unresolvable,         // Undeclared identifier
        Delegate // Fallback for complex cases (member access, etc.)
    }

    private readonly ReferenceKind _kind;

    // Identifier binding data
    private readonly JsEnvironment.ResolvedIdentifierBinding _binding;
    private readonly Symbol _name;
    private readonly EvaluationContext _context;
    private readonly bool _isStrict;

    // Global/With binding data
    private readonly ObjectEnvironmentBinding _globalBinding;
    private readonly JsEnvironment? _withFallbackEnvironment;

    // Delegate fallback for member access
    private readonly Func<object?>? _delegateGetter;
    private readonly Action<object?>? _delegateSetter;

    /// <summary>
    /// Creates a reference for a cached declarative binding (most common case).
    /// </summary>
    internal static AssignmentReference ForDeclarativeBinding(
        JsEnvironment.ResolvedIdentifierBinding binding,
        Symbol name,
        EvaluationContext context,
        bool isStrict)
    {
        return new AssignmentReference(
            ReferenceKind.DeclarativeBinding,
            binding,
            name,
            context,
            isStrict,
            default,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a reference for a global object binding.
    /// </summary>
    internal static AssignmentReference ForGlobalBinding(
        in ObjectEnvironmentBinding globalBinding,
        EvaluationContext context)
    {
        return new AssignmentReference(
            ReferenceKind.GlobalBinding,
            default,
            default,
            context,
            false,
            globalBinding,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a reference for a with statement binding.
    /// </summary>
    internal static AssignmentReference ForWithBinding(
        in ObjectEnvironmentBinding withBinding,
        JsEnvironment fallbackEnvironment,
        Symbol name,
        EvaluationContext context,
        bool isStrict)
    {
        return new AssignmentReference(
            ReferenceKind.WithBinding,
            default,
            name,
            context,
            isStrict,
            withBinding,
            fallbackEnvironment,
            null,
            null);
    }

    /// <summary>
    /// Creates a reference for an unresolvable identifier.
    /// </summary>
    internal static AssignmentReference ForUnresolvable(
        Symbol name,
        EvaluationContext context,
        bool isStrict,
        JsEnvironment environment)
    {
        return new AssignmentReference(
            ReferenceKind.Unresolvable,
            default,
            name,
            context,
            isStrict,
            default,
            environment,  // Store environment for sloppy mode global creation
            null,
            null);
    }

    /// <summary>
    /// Creates a reference using delegate fallback (for complex member access).
    /// </summary>
    internal static AssignmentReference ForDelegate(
        Func<object?> getter,
        Action<object?> setter)
    {
        return new AssignmentReference(
            ReferenceKind.Delegate,
            default,
            default,
            null!,
            false,
            default,
            null,
            getter,
            setter);
    }

    private AssignmentReference(
        ReferenceKind kind,
        JsEnvironment.ResolvedIdentifierBinding binding,
        Symbol name,
        EvaluationContext context,
        bool isStrict,
        in ObjectEnvironmentBinding globalBinding,
        JsEnvironment? withFallbackEnvironment,
        Func<object?>? delegateGetter,
        Action<object?>? delegateSetter)
    {
        _kind = kind;
        _binding = binding;
        _name = name;
        _context = context;
        _isStrict = isStrict;
        _globalBinding = globalBinding;
        _withFallbackEnvironment = withFallbackEnvironment;
        _delegateGetter = delegateGetter;
        _delegateSetter = delegateSetter;
    }

    public object? GetValue()
    {
        return _kind switch
        {
            ReferenceKind.DeclarativeBinding => ReadDeclarativeBinding(),
            ReferenceKind.GlobalBinding => ReadGlobalBinding(),
            ReferenceKind.WithBinding => ReadWithBinding(),
            ReferenceKind.Unresolvable => ReadUnresolvable(),
            ReferenceKind.Delegate => _delegateGetter!(),
            _ => throw new InvalidOperationException($"Unknown reference kind: {_kind}")
        };
    }

    /// <summary>
    /// Gets the value as JsValue, avoiding boxing for primitives in the declarative binding path.
    /// </summary>
    public JsValue GetJsValue()
    {
        return _kind switch
        {
            ReferenceKind.DeclarativeBinding => ReadDeclarativeBindingJsValue(),
            ReferenceKind.GlobalBinding => JsValue.FromObjectUnsafe(ReadGlobalBinding()),
            ReferenceKind.WithBinding => JsValue.FromObjectUnsafe(ReadWithBinding()),
            ReferenceKind.Unresolvable => JsValue.FromObjectUnsafe(ReadUnresolvable()),
            ReferenceKind.Delegate => JsValue.FromObjectUnsafe(_delegateGetter!()),
            _ => throw new InvalidOperationException($"Unknown reference kind: {_kind}")
        };
    }

    public void SetValue(JsValue value)
    {
        switch (_kind)
        {
            case ReferenceKind.DeclarativeBinding:
                WriteDeclarativeBinding(value);
                break;
            case ReferenceKind.GlobalBinding:
                WriteGlobalBinding(value);
                break;
            case ReferenceKind.WithBinding:
                WriteWithBinding(value);
                break;
            case ReferenceKind.Unresolvable:
                WriteUnresolvable(value);
                break;
            case ReferenceKind.Delegate:
                _delegateSetter!(ConvertJsValueToObject(value));
                break;
            default:
                throw new InvalidOperationException($"Unknown reference kind: {_kind}");
        }
    }

    private object? ReadDeclarativeBinding()
    {
        try
        {
            return _binding.Read(_name, _context);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context, _context.RealmState);
            _context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
            return Symbol.Undefined;
        }
    }

    private JsValue ReadDeclarativeBindingJsValue()
    {
        try
        {
            return _binding.ReadJsValue(_name, _context);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context, _context.RealmState);
            _context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
            return JsValue.Undefined;
        }
    }

    private void WriteDeclarativeBinding(JsValue value)
    {
        // Note: Don't rely on _context for write operations as it may be stale in async contexts.
        // The binding's environment has access to the RealmState for logging.
        _binding.WriteJsValue(value, _isStrict);
    }

    private object? ReadGlobalBinding()
    {
        try
        {
            return JsEnvironment.GetWithBindingValue(_globalBinding);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context, _context.RealmState);
            _context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
            return Symbol.Undefined;
        }
    }

    private void WriteGlobalBinding(JsValue value)
    {
        JsEnvironment.TrySetWithBindingValue(_globalBinding, ConvertJsValueToObject(value), _context.RealmState);
    }

    private object? ReadWithBinding()
    {
        try
        {
            return JsEnvironment.GetWithBindingValue(_globalBinding);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context, _context.RealmState);
            _context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
            return Symbol.Undefined;
        }
    }

    private void WriteWithBinding(JsValue value)
    {
        if (_isStrict && IsStrictRestrictedName(_name))
        {
            throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateSyntaxError(
                "Assignment to eval or arguments is not allowed in strict mode.", _context,
                _context.RealmState)));
        }

        var objValue = ConvertJsValueToObject(value);
        if (!JsEnvironment.TrySetWithBindingValue(_globalBinding, objValue, _context.RealmState))
        {
            _withFallbackEnvironment!.Assign(_name, objValue);
        }
    }

    private object? ReadUnresolvable()
    {
        try
        {
            return JsEnvironment.ReadUnresolvable(_name);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context, _context.RealmState);
            _context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
            return Symbol.Undefined;
        }
    }

    private void WriteUnresolvable(JsValue value)
    {
        JsEnvironment.AssignUnresolvable(_name, ConvertJsValueToObject(value), _isStrict, _context, _withFallbackEnvironment);
    }

    private static bool IsStrictRestrictedName(Symbol name)
    {
        return string.Equals(name.Name, "eval", StringComparison.Ordinal) ||
               string.Equals(name.Name, "arguments", StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts JsValue to object? for compatibility with methods that haven't been migrated yet.
    /// This manually expands the logic from ToObject() to avoid calling the obsolete method.
    /// </summary>
    private static object? ConvertJsValueToObject(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => Symbol.Undefined,
            JsValueKind.Null => null,
            JsValueKind.Boolean => JsValueCache.GetBoolean(value.NumberValue != 0.0),
            JsValueKind.Number => JsValueCache.GetNumber(value.NumberValue),
            JsValueKind.BigInt => value.ObjectValue,
            JsValueKind.String => value.ObjectValue,
            JsValueKind.Symbol => value.ObjectValue,
            JsValueKind.Object => value.ObjectValue,
            _ => Symbol.Undefined
        };
    }
}

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
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression,
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

            var binding = TypedAstEvaluator.ExpectSuperBinding(environment, context);
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

        if (target.IsNullish())
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
                    target,
                    propertyName,
                    context,
                    context.CurrentScope.IsStrict,
                    allowPrivate: !member.IsComputed);
            }
        }

        if (target is TypedArrayBase typedArray &&
            JsOps.TryResolveArrayIndex(propertyValue, out var typedIndex, context))
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
            target,
            propertyValue,
            context,
            context.CurrentScope.IsStrict,
            allowPrivate: !member.IsComputed);
        return AssignmentReference.ForDelegate(
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
            context.SetThrow(JsValue.FromObjectUnsafe(errorObject));
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
            target.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(receiver));
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

                TypedAstEvaluator.InvokeCallable(ownDescriptor.Set, [JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver), context);
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

            target.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(receiver));
            return;
        }

        var inheritedSetter = target.GetSetter(propertyName);
        if (inheritedSetter is not null)
        {
            TypedAstEvaluator.InvokeCallable(inheritedSetter, [JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver), context);
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

                        TypedAstEvaluator.InvokeCallable(inheritedDescriptor.Set, [JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver), context);
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

                        TypedAstEvaluator.InvokeCallable(inheritedDescriptor.Set, [JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver), context);
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

                prototypeAccessor.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(receiver));
                return;
            }

            prototypeAccessor.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(receiver));
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

        target.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(receiver));
    }
}

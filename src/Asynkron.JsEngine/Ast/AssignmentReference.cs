using Asynkron.JsEngine.JsTypes;
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
            return _binding.Read();
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
            return _binding.ReadJsValue(_context);
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

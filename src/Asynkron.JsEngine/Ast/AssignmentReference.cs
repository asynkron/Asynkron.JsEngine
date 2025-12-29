#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents a reference to an assignable location (lvalue).
/// Uses a discriminated union pattern to avoid delegate allocations for common identifier cases.
/// </summary>
internal readonly struct AssignmentReference
{
    private enum ReferenceKind
    {
        DeclarativeBinding, // Most common - cached identifier binding
        GlobalBinding, // Global object property
        WithBinding, // With statement binding
        Unresolvable, // Undeclared identifier
        Delegate // Fallback for complex cases (member access, etc.)
    }

    private readonly ReferenceKind _kind;

    // Identifier binding data
    private readonly JsEnvironment.ResolvedIdentifierBinding _binding;
    private readonly Symbol? _name;
    private readonly EvaluationContext? _context;
    private readonly bool _isStrict;

    // Global/With binding data
    private readonly ObjectEnvironmentBinding _globalBinding;
    private readonly JsEnvironment? _withFallbackEnvironment;

    // Delegate fallback for member access (JsValue-based)
    private readonly Func<JsValue>? _delegateGetterJs;
    private readonly Action<JsValue>? _delegateSetterJs;

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
            environment, // Store environment for sloppy mode global creation
            null,
            null);
    }

    /// <summary>
    /// Creates a reference using delegate fallback (for complex member access).
    /// </summary>
    internal static AssignmentReference ForDelegate(
        Func<JsValue> getter,
        Action<JsValue> setter)
    {
        return new AssignmentReference(
            ReferenceKind.Delegate,
            default,
            null,
            null,
            false,
            default,
            null,
            getter,
            setter);
    }

    private AssignmentReference(
        ReferenceKind kind,
        JsEnvironment.ResolvedIdentifierBinding binding,
        Symbol? name,
        EvaluationContext? context,
        bool isStrict,
        in ObjectEnvironmentBinding globalBinding,
        JsEnvironment? withFallbackEnvironment,
        Func<JsValue>? delegateGetterJs,
        Action<JsValue>? delegateSetterJs)
    {
        _kind = kind;
        _binding = binding;
        _name = name;
        _context = context;
        _isStrict = isStrict;
        _globalBinding = globalBinding;
        _withFallbackEnvironment = withFallbackEnvironment;
        _delegateGetterJs = delegateGetterJs;
        _delegateSetterJs = delegateSetterJs;
    }

    /// <summary>
    /// Gets the value as JsValue, avoiding boxing for primitives.
    /// </summary>
    public JsValue GetJsValue()
    {
        return _kind switch
        {
            ReferenceKind.DeclarativeBinding => ReadDeclarativeBindingJsValue(),
            ReferenceKind.GlobalBinding => ReadGlobalBindingJsValue(),
            ReferenceKind.WithBinding => ReadWithBindingJsValue(),
            ReferenceKind.Unresolvable => ReadUnresolvableJsValue(),
            ReferenceKind.Delegate => _delegateGetterJs!(),
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
                _delegateSetterJs!(value);
                break;
            default:
                throw new InvalidOperationException($"Unknown reference kind: {_kind}");
        }
    }

    private JsValue ReadDeclarativeBindingJsValue()
    {
        try
        {
            return _binding.ReadJsValue(_context!);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context!, _context!.RealmState);
            _context.SetThrow(errorObject);
            return JsValue.Undefined;
        }
    }

    private void WriteDeclarativeBinding(JsValue value)
    {
        // Note: Don't rely on _context for write operations as it may be stale in async contexts.
        // The binding's environment has access to the RealmState for logging.
        _binding.WriteJsValue(value, _isStrict);
    }

    private JsValue ReadGlobalBindingJsValue()
    {
        try
        {
            return JsEnvironment.GetWithBindingValueJsValue(_globalBinding);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context!, _context!.RealmState);
            _context.SetThrow(errorObject);
            return JsValue.Undefined;
        }
    }

    private void WriteGlobalBinding(JsValue value)
    {
        JsEnvironment.TrySetWithBindingValueJsValue(_globalBinding, value, _context!.RealmState);
    }

    private JsValue ReadWithBindingJsValue()
    {
        try
        {
            return JsEnvironment.GetWithBindingValueJsValue(_globalBinding);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context!, _context!.RealmState);
            _context.SetThrow(errorObject);
            return JsValue.Undefined;
        }
    }

    private void WriteWithBinding(JsValue value)
    {
        if (_isStrict && IsStrictRestrictedName(_name!))
        {
            throw new ThrowSignal(StandardLibrary.CreateSyntaxError(
                "Assignment to eval or arguments is not allowed in strict mode.", _context!,
                _context!.RealmState));
        }

        if (!JsEnvironment.TrySetWithBindingValueJsValue(_globalBinding, value, _context!.RealmState))
        {
            _withFallbackEnvironment!.AssignJsValue(_name!, value);
        }
    }

    private JsValue ReadUnresolvableJsValue()
    {
        try
        {
            return JsEnvironment.ReadUnresolvableJsValue(_name!);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, _context!, _context!.RealmState);
            _context.SetThrow(errorObject);
            return JsValue.Undefined;
        }
    }

    private void WriteUnresolvable(JsValue value)
    {
        JsEnvironment.AssignUnresolvableJsValue(_name!, value, _isStrict, _context!, _withFallbackEnvironment);
    }

    private static bool IsStrictRestrictedName(Symbol name)
    {
        return string.Equals(name.Name, "eval", StringComparison.Ordinal) ||
               string.Equals(name.Name, "arguments", StringComparison.Ordinal);
    }
}

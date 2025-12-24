#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Encapsulates property resolution (including private name scoping/branding)
///     and funnels get/set operations through the proper descriptor semantics.
///     This is a struct to avoid heap allocations in the hot path.
/// </summary>
public readonly struct PropertyHandle
{
    private readonly EvaluationContext _context;
    private readonly bool _isPrivate;
    private readonly bool _isStrict;
    private readonly string _propertyName;
    private readonly PrivateNameScope? _privateScope;
    private readonly JsValue _targetJsValue;

    private PropertyHandle(
        JsValue target,
        string propertyName,
        bool isPrivate,
        PrivateNameScope? privateScope,
        EvaluationContext context,
        bool isStrict)
    {
        _targetJsValue = target;
        _propertyName = propertyName;
        _isPrivate = isPrivate;
        _privateScope = privateScope;
        _context = context;
        _isStrict = isStrict;
    }

    public static PropertyHandle Resolve(
        JsValue target,
        string propertyName,
        EvaluationContext context,
        bool isStrict,
        bool allowPrivate = true)
    {
        if (context.ShouldStopEvaluation)
        {
            return new PropertyHandle(target, propertyName, false, null, context, isStrict);
        }

        var (resolvedName, isPrivate, privateScope) = allowPrivate
            ? ResolvePrivate(propertyName, context)
            : (propertyName, false, (PrivateNameScope?)null);
        return new PropertyHandle(target, resolvedName, isPrivate, privateScope, context, isStrict);
    }

    public static PropertyHandle Resolve(
        JsValue target,
        JsValue propertyValue,
        EvaluationContext context,
        bool isStrict,
        bool allowPrivate = true)
    {
        var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);
        return Resolve(target, propertyName, context, isStrict, allowPrivate);
    }

    public JsValue GetJsValue()
    {
        if (_context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        EnsurePrivateBrand();

        if (_targetJsValue.IsNullOrUndefined)
        {
            var errorMessage = _propertyName.Length > 0
                ? $"Cannot read property '{_propertyName}' of null or undefined"
                : "Cannot read properties of null or undefined";
            var error = StandardLibrary.CreateTypeError(
                errorMessage,
                _context,
                _context.RealmState);
            _context.SetThrow(error);
            return JsValue.Undefined;
        }

        // Use JsValue overload - returns JsValue directly, no conversion needed
        if (JsOps.TryGetPropertyValue(_targetJsValue, _propertyName, out var jsValue, _context))
        {
            return jsValue;
        }

        if (_context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (_isPrivate)
        {
            throw StandardLibrary.ThrowTypeError("Invalid access of private member", _context, _context.RealmState);
        }

        return JsValue.Undefined;
    }

    public void SetValue(JsValue value)
    {
        if (_context.ShouldStopEvaluation)
        {
            return;
        }

        EnsurePrivateBrand();
        TypedAstEvaluator.AssignPropertyValueWithNullCheck(_targetJsValue, _propertyName, value, _context, _isStrict);
    }

    public bool Delete()
    {
        if (_context.ShouldStopEvaluation)
        {
            return false;
        }

        EnsurePrivateBrand();

        if (_targetJsValue.IsNullOrUndefined)
        {
            throw StandardLibrary.ThrowTypeError("Cannot delete property on null or undefined", _context,
                _context.RealmState);
        }

        var deleted = JsOps.DeletePropertyValueJsValue(_targetJsValue, new JsValue(_propertyName), _context);
        if (!deleted && _isStrict)
        {
            throw StandardLibrary.ThrowTypeError("Cannot delete property", _context, _context.RealmState);
        }

        return deleted;
    }

    public bool Exists()
    {
        if (_context.ShouldStopEvaluation)
        {
            return false;
        }

        EnsurePrivateBrand();

        // Use JsValue overload for consistency
        return JsOps.TryGetPropertyValue(_targetJsValue, _propertyName, out _, _context);
    }

    private void EnsurePrivateBrand()
    {
        if (!_isPrivate)
        {
            return;
        }

        if (_privateScope is null)
        {
            throw StandardLibrary.ThrowTypeError("Invalid access of private member", _context, _context.RealmState);
        }

        var targetObj = _targetJsValue.ObjectValue;
        var hasBrand = targetObj is IPrivateBrandHolder brandHolder &&
                       brandHolder.HasPrivateBrand(_privateScope.BrandToken);

        _context.RealmState.Logger?.LogInformation(
            "Private member access targetType={TargetType} prop={PropertyName} hasBrand={HasBrand}",
            targetObj?.GetType().Name ?? "null",
            _propertyName,
            hasBrand);

        if (!hasBrand)
        {
            throw StandardLibrary.ThrowTypeError("Invalid access of private member", _context, _context.RealmState);
        }
    }

    private static (string ResolvedName, bool IsPrivate, PrivateNameScope? Scope) ResolvePrivate(
        string propertyName,
        EvaluationContext context)
    {
        if (!propertyName.IsPrivateName())
        {
            return (propertyName, false, null);
        }

        PrivateNameScope? privateScope = null;
        var resolvedKey = propertyName;
        var resolvedFromContext = false;
        var resolvedKeyFromContext = context.ResolvePrivateNameKey(propertyName);
        if (resolvedKeyFromContext is not null &&
            PrivateNameScope.TryResolveScope(resolvedKeyFromContext, out var resolvedScope))
        {
            resolvedKey = resolvedKeyFromContext;
            privateScope = resolvedScope;
            resolvedFromContext = true;
        }

        if (privateScope is null && propertyName.Contains('@', StringComparison.Ordinal))
        {
            PrivateNameScope.TryResolveScope(propertyName, out privateScope);
        }

        privateScope ??= context.CurrentPrivateNameScope;
        if (privateScope is null)
        {
            PrivateNameScope.TryResolveScope(propertyName, out privateScope);
        }

        if (privateScope is null)
        {
            throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
        }

        if (!resolvedFromContext && !propertyName.Contains('@', StringComparison.Ordinal))
        {
            resolvedKey = privateScope.GetKey(propertyName);
        }

        return (resolvedKey, true, privateScope);
    }
}

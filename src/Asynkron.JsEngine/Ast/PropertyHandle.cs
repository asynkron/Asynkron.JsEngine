using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    ///     Encapsulates property resolution (including private name scoping/branding)
    ///     and funnels get/set operations through the proper descriptor semantics.
    ///     This is a struct to avoid heap allocations in the hot path.
    /// </summary>
    internal readonly struct PropertyHandle
    {
        private readonly EvaluationContext _context;
        private readonly bool _isPrivate;
        private readonly bool _isStrict;
        private readonly string _propertyName;
        private readonly PrivateNameScope? _privateScope;
        private readonly object? _target;

        private PropertyHandle(
            object? target,
            string propertyName,
            bool isPrivate,
            PrivateNameScope? privateScope,
            EvaluationContext context,
            bool isStrict)
        {
            _target = target;
            _propertyName = propertyName;
            _isPrivate = isPrivate;
            _privateScope = privateScope;
            _context = context;
            _isStrict = isStrict;
        }

        public static PropertyHandle Resolve(
            object? target,
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
            object? target,
            JsValue propertyValue,
            EvaluationContext context,
            bool isStrict,
            bool allowPrivate = true)
        {
            var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);
            return Resolve(target, propertyName, context, isStrict, allowPrivate);
        }

        public object? GetValue()
        {
            if (_context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            EnsurePrivateBrand();

            if (IsNullish(_target))
            {
                var errorMessage = _propertyName.Length > 0
                    ? $"Cannot read property '{_propertyName}' of null or undefined"
                    : "Cannot read properties of null or undefined";
                var error = StandardLibrary.CreateTypeError(
                    errorMessage,
                    _context,
                    _context.RealmState);
                _context.SetThrow(JsValue.FromObjectUnsafe(error));
                return Symbol.Undefined;
            }

            if (TryGetPropertyValue(_target, _propertyName, out var value, _context))
            {
                return value;
            }

            if (_context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (_isPrivate)
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", _context, _context.RealmState);
            }

            return Symbol.Undefined;
        }

        public void SetValue(object? value)
        {
            if (_context.ShouldStopEvaluation)
            {
                return;
            }

            EnsurePrivateBrand();
            AssignPropertyValueWithNullCheck(_target, _propertyName, value, _context, _isStrict);
        }

        public bool Delete()
        {
            if (_context.ShouldStopEvaluation)
            {
                return false;
            }

            EnsurePrivateBrand();

            if (IsNullish(_target))
            {
                throw StandardLibrary.ThrowTypeError("Cannot delete property on null or undefined", _context,
                    _context.RealmState);
            }

            var deleted = JsOps.DeletePropertyValue(_target, _propertyName, _context);
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
            return TryGetPropertyValue(_target, _propertyName, out _, _context);
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

            var hasBrand = _target is IPrivateBrandHolder brandHolder &&
                           brandHolder.HasPrivateBrand(_privateScope.BrandToken);

            _context.RealmState.Logger?.LogInformation(
                "Private member access targetType={TargetType} prop={PropertyName} hasBrand={HasBrand}",
                _target?.GetType().Name ?? "null",
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
}

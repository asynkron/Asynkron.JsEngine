using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static AssignmentReference CreatePropertyReference(
        object? target,
        string propertyName,
        EvaluationContext context)
    {
        if (propertyName.IsPrivateName())
        {
            var privateScope = context.CurrentPrivateNameScope;
            if (privateScope is null)
            {
                PrivateNameScope.TryResolveScope(propertyName, out privateScope);
            }

            if (privateScope is null)
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context,
                    context.RealmState);
            }

            if (!propertyName.Contains("@", StringComparison.Ordinal))
            {
                propertyName = privateScope.GetKey(propertyName);
            }

            var brandToken = privateScope.BrandToken;
            if (target is not IPrivateBrandHolder brandHolder || !brandHolder.HasPrivateBrand(brandToken))
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context,
                    context.RealmState);
            }
        }

        return new AssignmentReference(
            () => ReadPropertyValue(target, propertyName, context),
            value => AssignPropertyValueWithNullCheck(target, propertyName, value, context));
    }

    private static object? ReadPropertyValue(object? target, string propertyName, EvaluationContext context)
    {
        if (IsNullish(target))
        {
            var errorMessage = propertyName.Length > 0
                ? $"Cannot read property '{propertyName}' of null or undefined"
                : "Cannot read properties of null or undefined";
            var error = StandardLibrary.CreateTypeError(
                errorMessage,
                context,
                context.RealmState);
            context.SetThrow(error);
            return Symbol.Undefined;
        }

        return TryGetPropertyValue(target, propertyName, out var existingValue, context)
            ? existingValue
            : Symbol.Undefined;
    }

    private static void AssignPropertyValueWithNullCheck(
        object? target,
        string propertyName,
        object? value,
        EvaluationContext context)
    {
        if (IsNullish(target))
        {
            context.RealmState?.Logger?.LogInformation("AssignPropertyValue nullish target property={PropertyName}",
                propertyName);
            var error = StandardLibrary.CreateTypeError(
                "Cannot set property on null or undefined.",
                context,
                context.RealmState);
            context.SetThrow(error);
            return;
        }

        if (target is JsObject jsObject)
        {
            AssignmentReferenceResolver.AssignObjectProperty(
                jsObject,
                propertyName,
                value,
                context.CurrentScope.IsStrict,
                context,
                context.RealmState,
                target);
            return;
        }

        if (target is IJsPropertyAccessor accessor)
        {
            var descriptor = accessor.GetOwnPropertyDescriptor(propertyName);
            if (descriptor is not null)
            {
                if (descriptor.IsAccessorDescriptor)
                {
                    if (descriptor.Set is null)
                    {
                        if (context.CurrentScope.IsStrict)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                $"Cannot set property '{propertyName}' that has only a getter.",
                                context,
                                context.RealmState);
                        }

                        return;
                    }

                    descriptor.Set.Invoke([value], target);
                    return;
                }

                if (!descriptor.Writable)
                {
                    if (context.CurrentScope.IsStrict)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot assign to read only property '{propertyName}'.",
                            context,
                            context.RealmState);
                    }

                    return;
                }

                accessor.SetProperty(propertyName, value, target);
                return;
            }

            if (target is IExtensibilityControl extensibility && !extensibility.IsExtensible)
            {
                if (context.CurrentScope.IsStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot add property '{propertyName}', object is not extensible.",
                        context,
                        context.RealmState);
                }

                return;
            }
        }

        AssignPropertyValue(target, propertyName, value, context);
    }
}

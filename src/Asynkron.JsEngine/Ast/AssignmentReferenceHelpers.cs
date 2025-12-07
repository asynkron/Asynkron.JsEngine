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
        EvaluationContext context,
        bool allowPrivate)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict,
            allowPrivate);

        return new AssignmentReference(
            () => handle.GetValue(),
            value => handle.SetValue(value));
    }

    private static void AssignPropertyValueWithNullCheck(
        object? target,
        string propertyName,
        object? value,
        EvaluationContext context)
    {
        AssignPropertyValueWithNullCheck(target, propertyName, value, context, context.CurrentScope.IsStrict);
    }

    private static void AssignPropertyValueWithNullCheck(
        object? target,
        string propertyName,
        object? value,
        EvaluationContext context,
        bool isStrict)
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
                isStrict,
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
                        if (isStrict)
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
                    if (isStrict)
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
                if (isStrict)
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

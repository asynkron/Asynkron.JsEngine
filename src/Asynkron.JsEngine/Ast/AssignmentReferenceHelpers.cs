using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

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

        // Per ES spec, [[Set]] on module namespace always returns false without triggering evaluation
        // Handle this early to avoid GetOwnPropertyDescriptor which would trigger evaluation
        if (target is ModuleNamespace)
        {
            if (isStrict)
            {
                throw StandardLibrary.ThrowTypeError(
                    "Module namespace objects are immutable",
                    context,
                    context.RealmState);
            }
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

        // Per ES spec 6.2.3.2 PutValue step 6.a: If HasPrimitiveBase(V) is true,
        // set base to ToObject(base). The receiver remains the original primitive.
        // This handles primitives like numbers, strings, booleans, and symbols.
        if (IsPrimitiveBase(target))
        {
            AssignPrimitiveProperty(target, propertyName, value, isStrict, context);
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

    /// <summary>
    ///     Returns true if the value is a primitive that has a wrapper object.
    ///     Per ES spec, this includes: string, number, boolean, symbol, and bigint.
    /// </summary>
    private static bool IsPrimitiveBase(object? target)
    {
        return target switch
        {
            string => true,
            bool => true,
            TypedAstSymbol => true,
            JsBigInt => true,
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => true,
            _ => false
        };
    }

    /// <summary>
    ///     Handles property assignment on primitive bases per ES spec 6.2.3.2 PutValue.
    ///     Converts the primitive to a wrapper object and attempts [[Set]] with the original
    ///     primitive as the receiver. The wrapper is temporary and changes don't persist.
    /// </summary>
    private static void AssignPrimitiveProperty(
        object? primitiveTarget,
        string propertyName,
        object? value,
        bool isStrict,
        EvaluationContext context)
    {
        var realm = context.RealmState;

        // ToObject: convert primitive to wrapper
        JsObject wrapper = primitiveTarget switch
        {
            string s => StandardLibrary.CreateStringWrapper(s, context, realm),
            bool b => CreateBooleanWrapper(b, realm),
            TypedAstSymbol sym => CreateSymbolWrapper(sym, realm),
            JsBigInt bi => StandardLibrary.CreateBigIntWrapper(bi, context, realm),
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte =>
                StandardLibrary.CreateNumberWrapper(JsOps.ToNumber(primitiveTarget), context, realm),
            _ => throw new InvalidOperationException($"Unexpected primitive type: {primitiveTarget?.GetType()}")
        };

        // Per ES spec, primitives are wrapped in non-extensible objects for [[Set]] purposes.
        // We need to perform the [[Set]] operation which may invoke setters from the prototype chain.
        // The receiver (thisValue) is the original primitive, not the wrapper.

        // Check for setters in the prototype chain first
        var setter = wrapper.GetSetter(propertyName);
        if (setter is not null)
        {
            // Invoke the setter with the original primitive as the receiver
            InvokeCallable(setter, [value], primitiveTarget, context);
            return;
        }

        // Check the prototype chain for inherited accessor properties with setters
        var prototype = wrapper.Prototype;
        while (prototype is not null)
        {
            var inheritedDescriptor = prototype.GetOwnPropertyDescriptor(propertyName);
            if (inheritedDescriptor is not null)
            {
                if (inheritedDescriptor.IsAccessorDescriptor)
                {
                    if (inheritedDescriptor.Set is not null)
                    {
                        // Invoke the setter with the original primitive as the receiver
                        InvokeCallable(inheritedDescriptor.Set, [value], primitiveTarget, context);
                        return;
                    }

                    // Accessor with no setter: fail silently in sloppy mode, throw in strict
                    if (isStrict)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot set property '{propertyName}' that has only a getter.",
                            context,
                            realm);
                    }

                    return;
                }

                // Data property found in prototype - primitives can't have own properties,
                // so we can't create a new own property. This is a silent failure in sloppy mode.
                if (isStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot create property '{propertyName}' on {GetPrimitiveTypeName(primitiveTarget)} '{primitiveTarget}'",
                        context,
                        realm);
                }

                return;
            }

            prototype = prototype.Prototype;
        }

        // No property found anywhere - trying to add a new property to a primitive.
        // This is a silent no-op in sloppy mode, TypeError in strict mode.
        if (isStrict)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot create property '{propertyName}' on {GetPrimitiveTypeName(primitiveTarget)} '{primitiveTarget}'",
                context,
                realm);
        }
    }

    private static JsObject CreateBooleanWrapper(bool value, RealmState? realm)
    {
        var obj = new JsObject();
        if (realm?.BooleanPrototype is not null)
        {
            obj.SetPrototype(realm.BooleanPrototype);
        }

        obj.SetProperty("__value__", value);
        return obj;
    }

    private static JsObject CreateSymbolWrapper(TypedAstSymbol symbol, RealmState? realm)
    {
        var obj = new JsObject();
        if (realm?.SymbolPrototype is not null)
        {
            obj.SetPrototype(realm.SymbolPrototype);
        }

        obj.SetProperty("__value__", symbol);
        return obj;
    }

    private static string GetPrimitiveTypeName(object? primitive)
    {
        return primitive switch
        {
            string => "string",
            bool => "boolean",
            TypedAstSymbol => "symbol",
            JsBigInt => "bigint",
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => "number",
            _ => "primitive"
        };
    }
}

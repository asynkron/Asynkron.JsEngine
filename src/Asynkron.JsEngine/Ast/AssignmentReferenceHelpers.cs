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
        if (!allowPrivate || !propertyName.IsPrivateName())
        {
            return AssignmentReference.ForDelegateJsValue(
                () =>
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
                        context.SetThrow(JsValue.FromObjectUnsafe(error));
                        return JsValue.Undefined;
                    }

                    return JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context)
                        ? JsValue.FromObjectUnsafe(directValue)
                        : JsValue.Undefined;
                },
                value => AssignPropertyValueWithNullCheck(target, propertyName, value, context,
                    context.CurrentScope.IsStrict));
        }

        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict,
            allowPrivate);

        return AssignmentReference.ForDelegateJsValue(
            () => handle.GetJsValue(),
            value => handle.SetValue(value));
    }

    private static void AssignPropertyValueWithNullCheck(
        object? target,
        string propertyName,
        JsValue value,
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
            context.SetThrow(JsValue.FromObjectUnsafe(error));
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

                    descriptor.Set.Invoke([value], JsValue.FromObjectUnsafe(target));
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

                accessor.SetProperty(propertyName, value, JsValue.FromObjectUnsafe(target));
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

        AssignPropertyValue(target, propertyName, ConvertJsValueToObject(value), context);
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
        JsValue value,
        bool isStrict,
        EvaluationContext context)
    {
        var realm = context.RealmState;

        // ToObject: convert primitive to wrapper
        var wrapper = primitiveTarget switch
        {
            string s => StringHelper.CreateStringWrapper(s, context, realm),
            bool b => CreateBooleanWrapper(b, realm),
            TypedAstSymbol sym => CreateSymbolWrapper(sym, realm),
            JsBigInt bi => BigIntHelper.CreateBigIntWrapper(bi, context, realm),
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte =>
                NumberHelper.CreateNumberWrapper(JsOps.ToNumber(primitiveTarget), context, realm),
            _ => throw new InvalidOperationException($"Unexpected primitive type: {primitiveTarget?.GetType()}")
        };

        // Per ES spec 6.2.3.2 PutValue, the [[Set]] operation is performed on the wrapper object
        // with the original primitive as the receiver. The wrapper object's SetProperty method
        // will handle the prototype chain lookup, including Proxy traps.
        //
        // Per ES spec OrdinarySet, when the receiver differs from the target (which is the case
        // for primitives where receiver is the primitive but target is the wrapper), and no
        // property is found on the receiver, [[Set]] returns false for creating own properties.
        //
        // We use the OrdinaryObjectInternalSlotOperations approach:
        // 1. Check if wrapper has the property (own or inherited)
        // 2. If inherited accessor with setter, invoke it with primitive receiver
        // 3. If trying to create a new own property on the primitive receiver, fail

        // Try to perform the [[Set]] operation through the wrapper's prototype chain
        var succeeded = TrySetPrimitiveProperty(wrapper, propertyName, value, primitiveTarget, context);

        // Per ES spec 6.2.3.2 step 6.c: If succeeded is false and IsStrictReference(V) is true,
        // throw a TypeError exception.
        if (!succeeded && isStrict)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot create property '{propertyName}' on {GetPrimitiveTypeName(primitiveTarget)} '{primitiveTarget}'",
                context,
                realm);
        }
    }

    /// <summary>
    ///     Attempts to set a property on a primitive's wrapper object with the primitive as the receiver.
    ///     Returns true if the operation succeeded (setter was invoked), false otherwise.
    /// </summary>
    private static bool TrySetPrimitiveProperty(
        JsObject wrapper,
        string propertyName,
        JsValue value,
        object? receiver,
        EvaluationContext context)
    {
        // First check if there's an own property on the wrapper
        var ownDescriptor = wrapper.GetOwnPropertyDescriptor(propertyName);
        if (ownDescriptor is not null)
        {
            if (ownDescriptor.IsAccessorDescriptor)
            {
                if (ownDescriptor.Set is not null)
                {
                    InvokeCallable(ownDescriptor.Set, [value], JsValue.FromObjectUnsafe(receiver), context);
                    return true;
                }
                // Accessor with only getter - [[Set]] returns false
                return false;
            }
            // Data property - primitives can't have own data properties that are writable
            // Return false since we can't create an own property on the receiver
            return false;
        }

        // Walk the prototype chain looking for properties
        // Use IJsPropertyAccessor to handle both JsObject and other types (like JsProxy)
        IJsPropertyAccessor? current = wrapper.Prototype;
        if (current is null && wrapper is IPrototypeAccessorProvider provider)
        {
            current = provider.PrototypeAccessor;
        }

        while (current is not null)
        {
            // Handle JsProxy specially - delegate [[Set]] to the proxy
            if (current is JsProxy proxy)
            {
                // Proxy's SetProperty will invoke the 'set' trap if defined
                // The trap receives (target, propertyName, value, receiver)
                try
                {
                    proxy.SetProperty(propertyName, value, JsValue.FromObjectUnsafe(receiver));
                    return true;
                }
                catch (ThrowSignal)
                {
                    // Proxy trap threw - re-throw
                    throw;
                }
            }

            var inheritedDescriptor = current.GetOwnPropertyDescriptor(propertyName);
            if (inheritedDescriptor is not null)
            {
                if (inheritedDescriptor.IsAccessorDescriptor)
                {
                    if (inheritedDescriptor.Set is not null)
                    {
                        InvokeCallable(inheritedDescriptor.Set, [value], JsValue.FromObjectUnsafe(receiver), context);
                        return true;
                    }
                    // Accessor with only getter - [[Set]] returns false
                    return false;
                }

                // Inherited data property found. Per ES spec, when receiver differs from the
                // object where the property was found, [[Set]] attempts to create an own
                // property on the receiver. But for primitives, this always fails.
                return false;
            }

            // Move to next prototype in the chain
            IJsPropertyAccessor? next = null;
            if (current is IJsObjectLike objectLike)
            {
                next = objectLike.Prototype;
            }
            if (next is null && current is IPrototypeAccessorProvider protoProvider)
            {
                next = protoProvider.PrototypeAccessor;
            }
            current = next;
        }

        // No property found in the chain. Per ES spec, [[Set]] would try to create
        // an own property on the receiver. For primitives, this is not possible.
        return false;
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

        obj.SetProperty("__value__", (JsValue)symbol);
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

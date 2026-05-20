#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static AssignmentReference CreatePropertyReference(
        JsValue target,
        string propertyName,
        EvaluationContext context,
        bool allowPrivate)
    {
        if (!allowPrivate || !propertyName.IsPrivateName())
        {
            return AssignmentReference.ForDelegate(
                () =>
                {
                    if (!target.IsNullOrUndefined)
                    {
                        return JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context)
                            ? directValue
                            : JsValue.Undefined;
                    }

                    var errorMessage = propertyName.Length > 0
                        ? $"Cannot read property '{propertyName}' of null or undefined"
                        : "Cannot read properties of null or undefined";
                    var error = StandardLibrary.CreateTypeError(
                        errorMessage,
                        context,
                        context.RealmState);
                    context.SetThrow(error);
                    return JsValue.Undefined;
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

        return AssignmentReference.ForDelegate(
            handle.GetJsValue,
            handle.SetValue);
    }

    internal static void AssignPropertyValueWithNullCheck(
        JsValue target,
        string propertyName,
        JsValue value,
        EvaluationContext context,
        bool isStrict)
    {
        // Check for null/undefined using JsValue before extracting to object
        if (target.IsNullOrUndefined)
        {
            context.RealmState.Logger?.LogInformation("AssignPropertyValue nullish target property={PropertyName}",
                propertyName);
            var error = StandardLibrary.CreateTypeError(
                "Cannot set property on null or undefined.",
                context,
                context.RealmState);
            context.SetThrow(error);
            return;
        }

        AssignPropertyValueWithNullCheckCore(target, propertyName, value, context, isStrict);
    }

    private static void AssignPropertyValueWithNullCheckCore(
        JsValue target,
        string propertyName,
        JsValue value,
        EvaluationContext context,
        bool isStrict)
    {
        // Per ES spec, [[Set]] on module namespace always returns false without triggering evaluation
        // Handle this early to avoid GetOwnPropertyDescriptor which would trigger evaluation
        if (target.TryGetObject<ModuleNamespace>(out _))
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

        if (target.TryGetObject<JsObject>(out var jsObject))
        {
            AssignmentReferenceResolver.AssignObjectProperty(
                jsObject,
                propertyName,
                value,
                isStrict,
                context,
                context.RealmState,
                jsObject);
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

        if (target.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (accessor is JsProxy proxy)
            {
                if (!proxy.TrySetProperty(propertyName, value, target) && isStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot assign to property '{propertyName}'.",
                        context,
                        context.RealmState);
                }

                return;
            }

            // ES2024 10.4.5.5: Check for TypedArray exotic [[Set]] in prototype chain.
            // This handles non-JsObject types (JsArray, etc.) that have a TypedArray in their chain.
            {
                var protoForCheck = accessor is IPrototypeAccessorProvider protoProvider
                    ? (IJsPropertyAccessor?)protoProvider.PrototypeAccessor
                    : accessor is IJsObjectLike objLike
                        ? objLike.Prototype
                        : null;
                var exoticResult = protoForCheck is not null
                    ? TypedArrayBase.CheckExoticSetInPrototypeChain(protoForCheck, propertyName)
                    : null;
                if (exoticResult == true)
                {
                    return; // Invalid index → silently succeed
                }

                if (exoticResult == false)
                {
                    // Valid index, O !== Receiver → create data property on receiver
                    if (accessor is IExtensibilityControl { IsExtensible: false })
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

                    if (accessor is IPropertyDefinitionHost defHost)
                    {
                        defHost.TryDefineProperty(propertyName,
                            new PropertyDescriptor
                            {
                                JsValue = value,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                    }
                    else
                    {
                        accessor.SetProperty(propertyName, value, target);
                    }

                    return;
                }
            }

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

                    descriptor.Set.Invoke(new SingleValueArgs(value), target);
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

                if (accessor is JsArray array && string.Equals(propertyName, "length", StringComparison.Ordinal))
                {
                    array.SetLength(value, context, throwOnWritableFailure: isStrict);
                    return;
                }

                accessor.SetProperty(propertyName, value, target);
                return;
            }

            if (accessor is IExtensibilityControl { IsExtensible: false })
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

        JsOps.AssignPropertyValueJsValue(target, new JsValue(propertyName), value, context);
    }

    /// <summary>
    ///     Returns true if the value is a primitive that has a wrapper object.
    ///     Per ES spec, this includes: string, number, boolean, symbol, and bigint.
    /// </summary>
    private static bool IsPrimitiveBase(JsValue target)
    {
        return target.Kind is JsValueKind.String or JsValueKind.Number or JsValueKind.Boolean
            or JsValueKind.Symbol or JsValueKind.BigInt;
    }

    /// <summary>
    ///     Handles property assignment on primitive bases per ES spec 6.2.3.2 PutValue.
    ///     Converts the primitive to a wrapper object and attempts [[Set]] with the original
    ///     primitive as the receiver. The wrapper is temporary and changes don't persist.
    /// </summary>
    private static void AssignPrimitiveProperty(
        JsValue primitiveTarget,
        string propertyName,
        JsValue value,
        bool isStrict,
        EvaluationContext context)
    {
        var realm = context.RealmState;

        // ToObject: convert primitive to wrapper
        var wrapper = primitiveTarget.Kind switch
        {
            JsValueKind.String => StringHelper.CreateStringWrapper(primitiveTarget.AsString(), context, realm),
            JsValueKind.Boolean => CreateBooleanWrapper(primitiveTarget.AsBoolean(), realm),
            JsValueKind.Symbol when primitiveTarget.ObjectValue is JsSymbol sym => CreateSymbolWrapper(sym, realm),
            JsValueKind.BigInt when primitiveTarget.ObjectValue is JsBigInt bi => BigIntHelper.CreateBigIntWrapper(bi,
                context, realm),
            JsValueKind.Number => NumberHelper.CreateNumberWrapper(primitiveTarget.NumberValue, context, realm),
            _ => throw new InvalidOperationException($"Unexpected primitive type: {primitiveTarget.Kind}")
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
        JsValue receiver,
        EvaluationContext context)
    {
        // First check if there's an own property on the wrapper
        var ownDescriptor = wrapper.GetOwnPropertyDescriptor(propertyName);
        if (ownDescriptor is not null)
        {
            if (!ownDescriptor.IsAccessorDescriptor)
            {
                return false;
            }

            if (ownDescriptor.Set is null)
            {
                return false;
            }

            InvokeCallableJsValue(ownDescriptor.Set, [value], receiver, context);
            return true;

            // Accessor with only getter - [[Set]] returns false

            // Data property - primitives can't have own data properties that are writable
            // Return false since we can't create an own property on the receiver
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
                proxy.SetProperty(propertyName, value, receiver);
                return true;
            }

            var inheritedDescriptor = current.GetOwnPropertyDescriptor(propertyName);
            if (inheritedDescriptor is not null)
            {
                if (!inheritedDescriptor.IsAccessorDescriptor)
                {
                    return false;
                }

                if (inheritedDescriptor.Set is null)
                {
                    return false;
                }

                InvokeCallableJsValue(inheritedDescriptor.Set, [value], receiver, context);
                return true;

                // Accessor with only getter - [[Set]] returns false

                // Inherited data property found. Per ES spec, when receiver differs from the
                // object where the property was found, [[Set]] attempts to create an own
                // property on the receiver. But for primitives, this always fails.
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

    private static JsObject CreateSymbolWrapper(JsSymbol symbol, RealmState? realm)
    {
        var obj = new JsObject();
        if (realm?.SymbolPrototype is not null)
        {
            obj.SetPrototype(realm.SymbolPrototype);
        }

        obj.SetProperty("__value__", (JsValue)symbol);
        return obj;
    }

    private static string GetPrimitiveTypeName(JsValue primitive)
    {
        return primitive.Kind switch
        {
            JsValueKind.String => "string",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Symbol => "symbol",
            JsValueKind.BigInt => "bigint",
            JsValueKind.Number => "number",
            _ => "primitive"
        };
    }
}

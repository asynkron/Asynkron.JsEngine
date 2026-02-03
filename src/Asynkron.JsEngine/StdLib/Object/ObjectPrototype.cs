#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ObjectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Object")]
public sealed partial class ObjectPrototype
{
    /// <summary>
    /// The intrinsic %Object.prototype.toString% function. Use this method when the ES spec
    /// requires calling the intrinsic rather than the user-modifiable Object.prototype.toString.
    /// </summary>
    internal static JsValue IntrinsicToString(JsValue thisValue) => ToString(thisValue);

    [JsHostMethod("toString", Length = 0d)]
    private static JsValue ToString(JsValue thisValue)
    {
        // ES spec: Object.prototype.toString
        // 1-3. Let O be ToObject(this value)
        // 4. Let isArray = IsArray(O)
        // 5-14. Determine builtinTag based on internal slots
        // 15. Let tag = Get(O, @@toStringTag)
        // 16. If Type(tag) is not String, set tag to builtinTag
        // 17. Return "[object " + tag + "]"

        // First, determine the builtinTag based on internal slots and type
        string builtinTag;
        if (thisValue.IsNull)
        {
            builtinTag = "Null";
        }
        else if (thisValue.IsUndefined)
        {
            builtinTag = "Undefined";
        }
        else if (thisValue.IsString)
        {
            builtinTag = "String";
        }
        else if (thisValue.IsNumber)
        {
            builtinTag = "Number";
        }
        else if (thisValue.IsBoolean)
        {
            builtinTag = "Boolean";
        }
        else if (thisValue.TryGetObject<JsArray>(out _))
        {
            builtinTag = "Array";
        }
        else if (thisValue.TryGetObject<JsArgumentsObject>(out _))
        {
            // ES spec: If O has [[ParameterMap]] internal slot, builtinTag is "Arguments"
            builtinTag = "Arguments";
        }
        else if (thisValue.TryGetObject<JsProxy>(out var proxy))
        {
            // ES spec: Proxies don't expose internal slots of their targets
            // A proxy is either callable (if target is callable) or not
            builtinTag = proxy.Target is IJsCallable ? "Function" : "Object";
        }
        else if (thisValue.TryGetObject<IJsCallable>(out _))
        {
            builtinTag = "Function";
        }
        else if (thisValue.TryGetObject<JsObject>(out var obj))
        {
            // Check for internal slots that indicate specific object types
            // ES spec step 9: [[ErrorData]] -> "Error"
            // ES spec step 10: [[BooleanData]] -> "Boolean"
            // ES spec step 11: [[NumberData]] -> "Number"
            // ES spec step 12: [[StringData]] -> "String"
            // ES spec step 13: [[DateValue]] -> "Date"
            // ES spec step 14: [[RegExpMatcher]] -> "RegExp"
            builtinTag = GetBuiltinTagFromInternalSlots(obj);
        }
        else
        {
            builtinTag = "Object";
        }

        // Now check for @@toStringTag - only use it if it's a string
        var tagKey = SymbolKeys.ToStringTag;
        if (thisValue.TryGetObject<JsObject>(out var objForTag))
        {
            if (objForTag.TryGetProperty(tagKey, out var tagValue) && tagValue.IsString)
            {
                return $"[object {tagValue.AsString()}]";
            }
        }
        else if (thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (accessor.TryGetProperty(tagKey, out var tagValue) && tagValue.IsString)
            {
                return $"[object {tagValue.AsString()}]";
            }
        }

        return $"[object {builtinTag}]";
    }

    /// <summary>
    /// Determines the builtin tag based on internal slots (ES spec steps 9-14).
    /// </summary>
    private static string GetBuiltinTagFromInternalSlots(JsObject obj)
    {
        // Check for [[ErrorData]] - set by ErrorConstructorBase.InitializeError
        if (obj.GetOwnPropertyDescriptor("_errorData") is not null)
        {
            return "Error";
        }

        // Check for [[DateValue]] - set by DateHelper.StoreInternalDateValue
        if (obj.GetOwnPropertyDescriptor("_internalDate") is not null)
        {
            return "Date";
        }

        // Check for [[RegExpMatcher]] - JsRegExp wrapper stored in __regex__
        if (obj.GetOwnPropertyDescriptor("__regex__") is { } regExpDescriptor &&
            regExpDescriptor.JsValue.TryGetObject<JsRegExp>(out _))
        {
            return "RegExp";
        }

        // Check for [[BooleanData]], [[NumberData]], [[StringData]] via __value__ property
        // and prototype chain
        if (obj.GetOwnPropertyDescriptor("__value__") is { } valueDesc)
        {
            // Determine which wrapper type by checking the prototype chain
            var proto = obj.Prototype;
            while (proto is not null)
            {
                if (proto.GetOwnPropertyDescriptor("constructor") is { } ctorDesc &&
                    ctorDesc.JsValue.TryGetObject<HostFunction>(out var ctor))
                {
                    if (ctor.TryGetProperty("name", out var nameValue) && nameValue.IsString)
                    {
                        var ctorName = nameValue.AsString();
                        if (ctorName == "Boolean") return "Boolean";
                        if (ctorName == "Number") return "Number";
                        if (ctorName == "String") return "String";
                    }
                }

                proto = proto.Prototype;
            }
        }

        return "Object";
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.valueOf called on null or undefined", realm: Realm);
        }

        return JsValue.FromObjectUnsafe(obj);
    }

    [JsHostMethod("hasOwnProperty", Length = 1d)]
    public JsValue HasOwnProperty(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return new JsValue(false);
        }

        var propertyName = JsOps.ToPropertyName(args[0]);
        if (propertyName is null)
        {
            return new JsValue(false);
        }

        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.hasOwnProperty called on null or undefined", realm: Realm);
        }

        var result = false;
        if (obj is JsObject jsObject)
        {
            result = jsObject.GetOwnPropertyDescriptor(propertyName) is not null;
        }
        else if (obj is JsArray array)
        {
            result = array.GetOwnPropertyDescriptor(propertyName) is not null;
        }
        else if (obj is IJsObjectLike accessor)
        {
            result = accessor.GetOwnPropertyDescriptor(propertyName) is not null;
        }

        return new JsValue(result);
    }

    [JsHostMethod("propertyIsEnumerable", Length = 1d)]
    private JsValue PropertyIsEnumerable(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return new JsValue(false);
        }

        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.propertyIsEnumerable called on null or undefined", realm: Realm);
        }

        var propertyName = JsOps.ToPropertyName(args[0]);
        if (propertyName is null)
        {
            return new JsValue(false);
        }

        if (obj is not IJsObjectLike accessor)
        {
            return new JsValue(false);
        }

        var desc = accessor.GetOwnPropertyDescriptor(propertyName);
        return new JsValue(desc?.Enumerable == true);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    private JsValue ToLocaleString(JsValue thisValue)
    {
        // ES spec 20.1.3.5: Object.prototype.toLocaleString ( [ reserved1 [ , reserved2 ] ] )
        // 1. Let O be the this value.
        // 2. Return ? Invoke(O, "toString").

        // RequireObjectCoercible: throw if null or undefined
        if (thisValue.IsNullOrUndefined)
        {
            throw ThrowTypeError("Object.prototype.toLocaleString called on null or undefined", realm: Realm);
        }

        // Invoke(O, "toString") = Call(GetV(O, "toString"), O)
        // GetV works with primitives by looking up on their prototype
        IJsPropertyAccessor? accessor;
        if (thisValue.TryGetObject<IJsPropertyAccessor>(out var obj))
        {
            accessor = obj;
        }
        else
        {
            // For primitives, get the wrapper prototype
            accessor = GetPrimitivePrototype(thisValue, Realm);
        }

        if (accessor is null || !accessor.TryGetProperty("toString", thisValue, out var toStringValue))
        {
            // Fallback to default toString
            return ToString(thisValue);
        }

        if (!toStringValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError("toString is not a function", realm: Realm);
        }

        // Call with primitive thisValue preserved (important for strict mode)
        return callable.Invoke([], thisValue);
    }

    private static IJsPropertyAccessor? GetPrimitivePrototype(JsValue value, RealmState? realm)
    {
        if (value.IsBoolean) return realm?.BooleanPrototype;
        if (value.IsNumber) return realm?.NumberPrototype;
        if (value.IsString) return realm?.StringPrototype;
        if (value.IsSymbol) return realm?.SymbolPrototype;
        if (value.IsBigInt) return realm?.BigIntPrototype;
        return realm?.ObjectPrototype;
    }

    [JsHostMethod("__lookupGetter__", Length = 1d)]
    private JsValue LookupGetter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("__lookupGetter__ called on null or undefined", realm: Realm);
        }

        var propertyName = JsOps.ToPropertyName(args.GetArgument(0));
        if (propertyName is null)
        {
            return JsValue.Undefined;
        }

        var cursor = obj;
        while (cursor is not null)
        {
            var desc = cursor.GetOwnPropertyDescriptor(propertyName);
            if (desc is not null)
            {
                if (!desc.IsAccessorDescriptor)
                {
                    return JsValue.Undefined;
                }

                return desc.Get is null ? JsValue.Undefined : JsValue.FromObjectUnsafe(desc.Get);
            }

            cursor = cursor.Prototype;
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("__lookupSetter__", Length = 1d)]
    private JsValue LookupSetter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("__lookupSetter__ called on null or undefined", realm: Realm);
        }

        var propertyName = JsOps.ToPropertyName(args.GetArgument(0));
        if (propertyName is null)
        {
            return JsValue.Undefined;
        }

        var cursor = obj;
        while (cursor is not null)
        {
            var desc = cursor.GetOwnPropertyDescriptor(propertyName);
            if (desc is not null)
            {
                if (!desc.IsAccessorDescriptor)
                {
                    return JsValue.Undefined;
                }

                return desc.Set is null ? JsValue.Undefined : JsValue.FromObjectUnsafe(desc.Set);
            }

            cursor = cursor.Prototype;
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("isPrototypeOf", Length = 1d)]
    private JsValue IsPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return JsValue.False;
        }

        if (!args[0].TryGetObject<IJsObjectLike>(out var objectLike))
        {
            return JsValue.False;
        }

        if (thisValue.IsNull || thisValue.IsUndefined)
        {
            object error;
            if (Realm.TypeErrorConstructor is IJsCallable ctor)
            {
                error = ctor.Invoke(new SingleValueArgs((JsValue)"Object.prototype.isPrototypeOf called on null or undefined"), JsValue.Null);
            }
            else
            {
                error = new InvalidOperationException("Object.prototype.isPrototypeOf called on null or undefined");
            }

            throw new ThrowSignal(JsValue.FromObjectUnsafe(error));
        }

        object? cursor = objectLike;
        while (TryGetPrototype(cursor, out var proto))
        {
            if (thisValue.TryGetObject<object>(out var thisObj) && ReferenceEquals(proto, thisObj))
            {
                return JsValue.True;
            }

            cursor = proto;
        }

        return JsValue.False;

        static bool TryGetPrototype(object? candidate, out IJsObjectLike? prototype)
        {
            prototype = null;

            switch (candidate)
            {
                case JsProxy proxy:
                    {
                        var proxyProto = proxy.GetPrototypeWithTrap();
                        if (proxyProto is null)
                        {
                            return false;
                        }

                        prototype = proxyProto as IJsObjectLike;
                        return prototype is not null;
                    }
                case IJsObjectLike { Prototype: { } protoObj }:
                    prototype = protoObj;
                    return true;
                case IPrototypeAccessorProvider { PrototypeAccessor: { } protoAccessor }:
                    {
                        prototype = protoAccessor as IJsObjectLike;
                        if (prototype is not null)
                        {
                            return true;
                        }

                        break;
                    }
            }

            if (candidate is not JsObject jsObj || !jsObj.TryGetProperty("__proto__", out var protoProp))
            {
                return false;
            }

            if (!protoProp.TryGetObject<IJsObjectLike>(out var protoFromProp))
            {
                return false;
            }

            prototype = protoFromProp;
            return true;
        }
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject objectProto)
        {
            Realm.ObjectPrototype ??= objectProto;

            // Object.prototype is an immutable prototype exotic object per ES spec 9.4.7
            // Its [[SetPrototypeOf]] returns false for any value other than null (its current prototype)
            objectProto.IsImmutablePrototype = true;

            Realm.FunctionPrototype?.SetPrototype(objectProto);
            Realm.BooleanPrototype?.SetPrototype(objectProto);
            Realm.NumberPrototype?.SetPrototype(objectProto);
            Realm.StringPrototype?.SetPrototype(objectProto);

            if (Realm.ErrorPrototype is not null && Realm.ErrorPrototype.Prototype is null)
            {
                Realm.ErrorPrototype.SetPrototype(objectProto);
            }
            // __proto__ getter/setter is registered via code generation from attributes
        }
    }

    [JsHostGetter("__proto__", DisplayName = "get __proto__")]
    private JsValue GetProto(JsValue thisValue)
    {
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.__proto__ called on null or undefined", realm: Realm);
        }

        if (obj is JsProxy proxy)
        {
            return JsValue.FromObjectUnsafe(proxy.GetPrototypeWithTrap());
        }

        object? proto = obj.Prototype;
        if (proto is null && obj is IPrototypeAccessorProvider provider)
        {
            proto = provider.PrototypeAccessor;
        }

        return proto is null ? JsValue.Null : JsValue.FromObjectUnsafe(proto);
    }

    [JsHostSetter("__proto__", DisplayName = "set __proto__")]
    private JsValue SetProto(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var newProto = args.GetArgument(0);
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.__proto__ called on null or undefined", realm: Realm);
        }

        IJsPropertyAccessor? protoToSet = null;
        if (!newProto.IsNull)
        {
            if (!newProto.TryGetObject<IJsPropertyAccessor>(out var protoAccessor))
            {
                return JsValue.Undefined;
            }

            protoToSet = protoAccessor;
        }

        // ES spec §B.2.2.1.2 (set Object.prototype.__proto__):
        // 4. Let status be ? O.[[SetPrototypeOf]](proto).
        // 5. If status is false, throw a TypeError exception.
        try
        {
            if (WouldCreatePrototypeCycle(obj, protoToSet))
            {
                throw ThrowTypeError("Cyclic __proto__ value", realm: Realm);
            }

            obj.SetPrototype(protoToSet);
        }
        catch (ThrowSignal ex)
        {
            // [[SetPrototypeOf]] returned false (e.g., for immutable prototype exotic objects)
            // Per spec step 5, throw TypeError
            if (ex.ThrownValue.IsUndefined)
            {
                throw ThrowTypeError("Cannot set prototype of object", realm: Realm);
            }

            throw;
        }

        return JsValue.Undefined;
    }

    private static bool WouldCreatePrototypeCycle(IJsObjectLike target, IJsPropertyAccessor? protoAccessor)
    {
        if (protoAccessor is null)
        {
            return false;
        }

        IJsPropertyAccessor? current = protoAccessor;
        for (var depth = 0; current is not null && depth < JsEngineConstants.MaxPrototypeChainDepth; depth++)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = current switch
            {
                JsProxy proxy => proxy.GetPrototypeWithTrap(),
                JsObject jsObject => jsObject.Prototype,
                IPrototypeAccessorProvider provider => provider.PrototypeAccessor,
                _ => null
            };
        }

        return false;
    }
}

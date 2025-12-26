#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ObjectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Object")]
public sealed partial class ObjectPrototype
{
    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    private static JsValue ToString(JsValue thisValue)
    {
        var tagKey = SymbolKeys.ToStringTag;
        if (thisValue.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty(tagKey, out var tagValue) && !tagValue.IsUndefined)
            {
                var tagString = JsOps.ToJsString(tagValue);
                return $"[object {tagString}]";
            }
        }
        else if (thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (accessor.TryGetProperty(tagKey, out var tagValue) && !tagValue.IsUndefined)
            {
                var tagString = JsOps.ToJsString(tagValue);
                return $"[object {tagString}]";
            }
        }

        string tag;
        if (thisValue.IsNull)
        {
            tag = "Null";
        }
        else if (thisValue.IsUndefined)
        {
            tag = "Undefined";
        }
        else if (thisValue.IsString)
        {
            tag = "String";
        }
        else if (thisValue.IsNumber)
        {
            tag = "Number";
        }
        else if (thisValue.IsBoolean)
        {
            tag = "Boolean";
        }
        else if (thisValue.TryGetObject<JsArray>(out _))
        {
            tag = "Array";
        }
        else if (thisValue.TryGetObject<IJsCallable>(out _))
        {
            tag = "Function";
        }
        else
        {
            tag = "Object";
        }

        return $"[object {tag}]";
    }

    /* FLAKY */
    [JsHostMethod("valueOf", Length = 0d)]
    public static JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return thisValue;
    }

    /* FLAKY */
    [JsHostMethod("hasOwnProperty", Length = 1d)]
    public static JsValue HasOwnProperty(JsValue thisValue, IReadOnlyList<JsValue> args)
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

        var result = false;
        if (thisValue.TryGetObject<JsObject>(out var obj))
        {
            result = obj.GetOwnPropertyDescriptor(propertyName) is not null;
        }
        else if (thisValue.TryGetObject<JsArray>(out var array))
        {
            result = array.GetOwnPropertyDescriptor(propertyName) is not null;
        }
        else if (thisValue.TryGetObject<IJsObjectLike>(out var accessor))
        {
            result = accessor.GetOwnPropertyDescriptor(propertyName) is not null;
        }

        return new JsValue(result);
    }

    /* FLAKY */
    [JsHostMethod("propertyIsEnumerable", Length = 1d)]
    private static JsValue PropertyIsEnumerable(JsValue thisValue, IReadOnlyList<JsValue> args)
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

        if (!thisValue.TryGetObject<IJsObjectLike>(out var accessor))
        {
            return new JsValue(false);
        }

        var desc = accessor.GetOwnPropertyDescriptor(propertyName);
        return new JsValue(desc?.Enumerable == true);
    }

    /* FLAKY */
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

    /* FLAKY */
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

    /* FLAKY */
    [JsHostMethod("isPrototypeOf", Length = 1d)]
    private JsValue IsPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsNull || thisValue.IsUndefined)
        {
            object error;
            if (Realm.TypeErrorConstructor is IJsCallable ctor)
            {
                error = ctor.Invoke(["Object.prototype.isPrototypeOf called on null or undefined"], JsValue.Null);
            }
            else
            {
                error = new InvalidOperationException("Object.prototype.isPrototypeOf called on null or undefined");
            }

            throw new ThrowSignal(JsValue.FromObjectUnsafe(error));
        }

        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return JsValue.False;
        }

        if (!args[0].TryGetObject<IJsObjectLike>(out var objectLike))
        {
            return JsValue.False;
        }

        var cursor = objectLike as object;
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

        if (!IsTargetExtensible(obj))
        {
            return JsValue.Undefined;
        }

        obj.SetPrototype(protoToSet);
        return JsValue.Undefined;
    }
}

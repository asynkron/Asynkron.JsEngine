using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Object")]
public sealed partial class ObjectPrototype : JsPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var tagKey = SymbolKeys.GetToStringTag(Realm);
        if (thisValue.TryGetObject<JsObject>(out var obj) && obj is not null)
        {
            if (obj.TryGetProperty(tagKey, out var tagValue) && !tagValue.IsUndefined)
            {
                var tagString = JsOps.ToJsString(tagValue);
                return $"[object {tagString}]";
            }
        }
        else if (thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor) && accessor is not null)
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
        else if (thisValue.TryGetObject<JsArray>(out var _))
        {
            tag = "Array";
        }
        else if (thisValue.TryGetObject<IJsCallable>(out var __))
        {
            tag = "Function";
        }
        else
        {
            tag = "Object";
        }

        return $"[object {tag}]";
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return thisValue;
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

        var result = false;
        if (thisValue.TryGetObject<JsObject>(out var obj) && obj is not null)
        {
            result = obj.GetOwnPropertyDescriptor(propertyName) is not null;
        }
        else if (thisValue.TryGetObject<JsArray>(out var array) && array is not null)
        {
            result = array.GetOwnPropertyDescriptor(propertyName) is not null;
        }
        else if (thisValue.TryGetObject<IJsObjectLike>(out var accessor) && accessor is not null)
        {
            result = accessor.GetOwnPropertyDescriptor(propertyName) is not null;
        }

        return new JsValue(result);
    }

    [JsHostMethod("propertyIsEnumerable", Length = 1d)]
    public JsValue PropertyIsEnumerable(JsValue thisValue, IReadOnlyList<JsValue> args)
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

        if (!thisValue.TryGetObject<IJsObjectLike>(out var accessor) || accessor is null)
        {
            return new JsValue(false);
        }

        var desc = accessor.GetOwnPropertyDescriptor(propertyName);
        return new JsValue(desc?.Enumerable == true);
    }

    [JsHostMethod("__lookupGetter__", Length = 1d)]
    public JsValue LookupGetter(JsValue thisValue, IReadOnlyList<JsValue> args)
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
    public JsValue LookupSetter(JsValue thisValue, IReadOnlyList<JsValue> args)
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
    public JsValue IsPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
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
            return new JsValue(false);
        }

        if (!args[0].TryGetObject<IJsObjectLike>(out var objectLike))
        {
            return new JsValue(false);
        }

        var cursor = objectLike as object;
        while (TryGetPrototype(cursor, out var proto))
        {
            if (thisValue.TryGetObject<object>(out var thisObj) && ReferenceEquals(proto, thisObj))
            {
                return new JsValue(true);
            }

            cursor = proto;
        }

        return new JsValue(false);

        static bool TryGetPrototype(object? candidate, out IJsObjectLike? prototype)
        {
            prototype = null;

            if (candidate is IJsObjectLike objLike && objLike.Prototype is { } protoObj)
            {
                prototype = protoObj;
                return true;
            }

            if (candidate is IPrototypeAccessorProvider { PrototypeAccessor: { } protoAccessor })
            {
                prototype = protoAccessor as IJsObjectLike;
                if (prototype is not null)
                {
                    return true;
                }
            }

            if (candidate is JsObject jsObj && jsObj.TryGetProperty("__proto__", out var protoProp))
            {
                if (protoProp.TryGetObject<IJsObjectLike>(out var protoFromProp))
                {
                    prototype = protoFromProp;
                    return true;
                }
            }

            return false;
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

            var protoGetter = new HostFunction(GetProto, Realm, isConstructor: false);
            protoGetter.TryDefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "get __proto__",
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });
            var protoSetter = new HostFunction(SetProto, Realm, isConstructor: false);
            protoSetter.TryDefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "set __proto__",
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });

            objectProto.DefineProperty("__proto__", new PropertyDescriptor
            {
                Get = protoGetter,
                Set = protoSetter,
                Enumerable = false,
                Configurable = true
            });
        }
    }

    private JsValue GetProto(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = args;
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

    private JsValue SetProto(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var newProto = args.GetArgument(0);
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.__proto__ called on null or undefined", realm: Realm);
        }

        object? protoToSet = null;
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

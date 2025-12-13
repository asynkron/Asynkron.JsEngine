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
    public object? ToString(object? thisValue, IReadOnlyList<object?> _)
    {
        var tagKey = SymbolKeys.GetToStringTag(Realm);
        if (thisValue is JsObject obj)
        {
            if (obj.TryGetProperty(tagKey, out var tagValue) && !ReferenceEquals(tagValue, Symbol.Undefined))
            {
                var tagString = JsOps.ToJsString(tagValue);
                return $"[object {tagString}]";
            }
        }
        else if (thisValue is IJsPropertyAccessor accessor)
        {
            if (accessor.TryGetProperty(tagKey, out var tagValue) &&
                !ReferenceEquals(tagValue, Symbol.Undefined))
            {
                var tagString = JsOps.ToJsString(tagValue);
                return $"[object {tagString}]";
            }
        }

        var tag = thisValue switch
        {
            null => "Null",
            JsObject => "Object",
            JsArray => "Array",
            string => "String",
            double => "Number",
            bool => "Boolean",
            IJsCallable => "Function",
            _ when ReferenceEquals(thisValue, Symbol.Undefined) => "Undefined",
            _ => "Object"
        };

        return $"[object {tag}]";
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public object? ValueOf(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue;
    }

    [JsHostMethod("hasOwnProperty", Length = 1d)]
    public object? HasOwnProperty(object? thisValue, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        var propertyName = JsOps.ToPropertyName(args[0]);
        if (propertyName is null)
        {
            return false;
        }

        return thisValue switch
        {
            JsObject obj => obj.GetOwnPropertyDescriptor(propertyName) is not null,
            JsArray array => array.GetOwnPropertyDescriptor(propertyName) is not null,
            IJsObjectLike accessor => accessor.GetOwnPropertyDescriptor(propertyName) is not null,
            _ => false
        };
    }

    [JsHostMethod("propertyIsEnumerable", Length = 1d)]
    public object? PropertyIsEnumerable(object? thisValue, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        var propertyName = JsOps.ToPropertyName(args[0]);
        if (propertyName is null)
        {
            return false;
        }

        if (thisValue is not IJsObjectLike accessor)
        {
            return false;
        }

        var desc = accessor.GetOwnPropertyDescriptor(propertyName);
        return desc?.Enumerable == true;
    }

    [JsHostMethod("isPrototypeOf", Length = 1d)]
    public object? IsPrototypeOf(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is null || ReferenceEquals(thisValue, Symbol.Undefined))
        {
            var error = Realm.TypeErrorConstructor is IJsCallable ctor
                ? ctor.Invoke(["Object.prototype.isPrototypeOf called on null or undefined"], null)
                : new InvalidOperationException("Object.prototype.isPrototypeOf called on null or undefined");
            throw new ThrowSignal(error);
        }

        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            return false;
        }

        if (args[0] is not IJsObjectLike objectLike)
        {
            return false;
        }

        var cursor = objectLike as object;
        while (TryGetPrototype(cursor, out var proto))
        {
            if (ReferenceEquals(proto, thisValue))
            {
                return true;
            }

            cursor = proto;
        }

        return false;

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

            if (candidate is JsObject jsObj && jsObj.TryGetProperty("__proto__", out var protoProp) &&
                protoProp is IJsObjectLike protoFromProp)
            {
                prototype = protoFromProp;
                return true;
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

    private object? GetProto(object? thisValue, IReadOnlyList<object?> args)
    {
        _ = args;
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.__proto__ called on null or undefined", realm: Realm);
        }

        if (obj is JsProxy proxy)
        {
            return proxy.GetPrototypeWithTrap();
        }

        object? proto = obj.Prototype;
        if (proto is null && obj is IPrototypeAccessorProvider provider)
        {
            proto = provider.PrototypeAccessor;
        }

        return proto;
    }

    private object? SetProto(object? thisValue, IReadOnlyList<object?> args)
    {
        var newProto = args.GetArgument(0);
        if (!TryGetObject(thisValue, Realm, out var obj))
        {
            throw ThrowTypeError("Object.prototype.__proto__ called on null or undefined", realm: Realm);
        }

        if (newProto is not IJsPropertyAccessor && newProto is not null)
        {
            return Symbol.Undefined;
        }

        if (!IsTargetExtensible(obj))
        {
            return Symbol.Undefined;
        }

        obj.SetPrototype(newProto);
        return Symbol.Undefined;
    }
}

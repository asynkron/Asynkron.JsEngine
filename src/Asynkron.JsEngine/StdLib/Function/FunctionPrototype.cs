using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Function", ObjectKind = PrototypeObjectKind.Function)]
public sealed partial class FunctionPrototype : JsPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public object ToString(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue switch
        {
            IJsCallable => "function() { [native code] }",
            _ => "function undefined() { [native code] }"
        };
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public object? ValueOf(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue;
    }

    [JsHostMethod("call", Length = 1d)]
    public object? Call(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not IJsCallable target)
        {
            return Symbol.Undefined;
        }

        var thisArg = args.GetArgument(0);
        var callArgs = args.SliceFrom(1);
        return target.Invoke(callArgs, thisArg);
    }

    protected override void ConfigurePrototype()
    {
        // Seed the intrinsic prototype slot before any RealmState-based prototype resolution runs.
        Realm.FunctionPrototype ??= Prototype;

        if (Prototype is IJsPropertyAccessor accessor)
        {
            DefineConstantProperty(accessor, "length", 0d, configurable: true);
        }

        AttachCallerAccessors();
        AttachArgumentsPoison();
        AttachHasInstance();
    }

    private void AttachCallerAccessors()
    {
        var callerGetter = new HostFunction((thisValue, _) =>
        {
            if (thisValue is not ICallerInfo callerInfo)
            {
                throw ThrowTypeError("Function.prototype.caller called on non-callable", realm: Realm);
            }

            var isArrowFunction = callerInfo is ICallableMetadata { IsArrowFunction: true };
            if (callerInfo.IsStrictFunction || isArrowFunction)
            {
                throw ThrowTypeError("Access to caller or arguments is not allowed", realm: Realm);
            }

            if (callerInfo.Caller is ICallerInfo callerMetadata && callerMetadata.IsStrictFunction)
            {
                throw ThrowTypeError("Access to caller or arguments is not allowed", realm: Realm);
            }

            return (object?)callerInfo.Caller ?? Symbol.Undefined;
        }, Realm, isConstructor: false);

        var callerSetter = new HostFunction((thisValue, _) =>
        {
            var isArrowFunction = thisValue is ICallableMetadata { IsArrowFunction: true };
            if (thisValue is not ICallerInfo callerInfo || callerInfo.IsStrictFunction || isArrowFunction)
            {
                throw ThrowTypeError("Access to caller or arguments is not allowed", realm: Realm);
            }

            // Legacy accessor is effectively non-writable; ignore assignments in sloppy mode.
            return Symbol.Undefined;
        }, Realm, isConstructor: false);

        var callerDescriptor = new PropertyDescriptor
        {
            Get = callerGetter, Set = callerSetter, Enumerable = false, Configurable = false
        };

        Prototype.DefineProperty("caller", callerDescriptor);
    }

    private void AttachArgumentsPoison()
    {
        var thrower = new HostFunction((_, _) =>
            throw ThrowTypeError("Access to caller or arguments is not allowed", realm: Realm), Realm,
            isConstructor: false);
        var poisonDescriptor = new PropertyDescriptor
        {
            Get = thrower, Set = thrower, Enumerable = false, Configurable = false
        };
        Prototype.DefineProperty("arguments", poisonDescriptor);
    }

    private void AttachHasInstance()
    {
        var hasInstanceKey = $"@@symbol:{TypedAstSymbol.For("Symbol.hasInstance").GetHashCode()}";
        var hasInstance = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsPropertyAccessor accessor)
            {
                throw ThrowTypeError("Function.prototype[@@hasInstance] called on non-object", realm: Realm);
            }

            var candidate = args.GetArgument(0);
            if (candidate is not JsObject && candidate is not IJsObjectLike)
            {
                return false;
            }

            if (!JsOps.TryGetPropertyValue(accessor, "prototype", out var protoVal) ||
                protoVal is not IJsPropertyAccessor prototypeObject)
            {
                throw ThrowTypeError("Function has non-object prototype in instanceof check", realm: Realm);
            }

            var cursor = JsOps.GetPrototypePointer(candidate);
            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, prototypeObject))
                {
                    return true;
                }

                cursor = JsOps.GetPrototypePointer(cursor);
            }

            return false;
        }, Realm, isConstructor: false);

        hasInstance.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "[Symbol.hasInstance]", Writable = false, Enumerable = false, Configurable = true
            });
        hasInstance.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });

        Prototype.DefineProperty(hasInstanceKey,
            new PropertyDescriptor
            {
                Value = hasInstance, Writable = false, Enumerable = false, Configurable = false
            });
    }
}

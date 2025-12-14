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
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        if (thisValue.TryGetObject<IJsCallable>(out _))
        {
            return new JsValue("function() { [native code] }");
        }
        return new JsValue("function undefined() { [native code] }");
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return thisValue;
    }

    [JsHostMethod("call", Length = 1d)]
    public JsValue Call(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<IJsCallable>(out var target))
        {
            return JsValue.Undefined;
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

        AttachArgumentsPoison();
        AttachHasInstance();
    }

    private void AttachArgumentsPoison()
    {
        // ES spec requires "caller" and "arguments" to be "poison pill" accessors
        // on Function.prototype that throw TypeError when accessed.
        // See ECMA-262 AddRestrictedFunctionProperties
        var thrower = new HostFunction((JsValue _, IReadOnlyList<JsValue> _) =>
            throw ThrowTypeError("'caller' and 'arguments' are restricted function properties and cannot be accessed in this context.", realm: Realm), Realm,
            isConstructor: false);
        var poisonDescriptor = new PropertyDescriptor
        {
            Get = thrower, Set = thrower, Enumerable = false, Configurable = true
        };
        Prototype.DefineProperty("caller", poisonDescriptor);
        Prototype.DefineProperty("arguments", poisonDescriptor);
    }

    private void AttachHasInstance()
    {
        var hasInstanceKey = SymbolKeys.GetHasInstance(Realm);
        var hasInstance = new HostFunction((JsValue thisValue, IReadOnlyList<JsValue> args) =>
        {
            if (!thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                throw ThrowTypeError("Function.prototype[@@hasInstance] called on non-object", realm: Realm);
            }

            var candidate = args.GetArgument(0);
            if (!candidate.TryGetObject<JsObject>(out _) && !candidate.TryGetObject<IJsObjectLike>(out _))
            {
                return new JsValue(false);
            }

            if (!JsOps.TryGetPropertyValue(accessor, "prototype", out var protoVal) ||
                !protoVal.TryGetObject<IJsPropertyAccessor>(out var prototypeObject))
            {
                throw ThrowTypeError("Function has non-object prototype in instanceof check", realm: Realm);
            }

            var cursor = JsOps.GetPrototypePointer(candidate);
            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, prototypeObject))
                {
                    return new JsValue(true);
                }

                cursor = JsOps.GetPrototypePointer(cursor);
            }

            return new JsValue(false);
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

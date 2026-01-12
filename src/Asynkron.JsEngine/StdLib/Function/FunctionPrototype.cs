#region

using System.Collections.Generic;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Function", ObjectKind = PrototypeObjectKind.Function)]
public sealed partial class FunctionPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    private static JsValue ToString(JsValue thisValue)
    {
        if (thisValue.TryGetObject<IJsCallable>(out _))
        {
            return new JsValue("function() { [native code] }");
        }

        return new JsValue("function undefined() { [native code] }");
    }

    [JsHostMethod("valueOf", Length = 0d)]
    private static JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return thisValue;
    }

    [JsHostMethod("call", Length = 1d)]
    private static JsValue Call(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<IJsCallable>(out var target))
        {
            return JsValue.Undefined;
        }

        var thisArg = args.GetArgument(0);
        var callArgs = args.SliceFrom(1);
        return target.Invoke(callArgs, thisArg);
    }

    [JsHostMethod("apply", Length = 2d)]
    private static JsValue Apply(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<IJsCallable>(out var target))
        {
            var realmState = thisValue.TryGetObject<ICallableMetadata>(out var metadata)
                ? metadata.RealmState
                : null;
            throw ThrowTypeError("Function.prototype.apply called on incompatible receiver", realm: realmState);
        }

        var thisArg = args.GetArgument(0);
        var argArray = args.Count > 1 ? args[1] : JsValue.Undefined;
        if (argArray.IsNullOrUndefined)
        {
            return target.Invoke(ArgumentSlice.Empty, thisArg);
        }

        var realmStateForArray = (target as ICallableMetadata)?.RealmState;
        var accessor = EnsureArrayLikeReceiver(argArray, "Function.prototype.apply", realmStateForArray);
        var length = LengthOfArrayLike(accessor, realmStateForArray);
        if (length > int.MaxValue)
        {
            throw ThrowRangeError("Function.prototype.apply arguments exceed supported length", realm: realmStateForArray);
        }

        var list = new List<JsValue>((int)length);
        for (var i = 0L; i < length; i++)
        {
            list.Add(GetElementOrUndefinedJsValue(accessor, ToIndexString(i)));
        }

        return target.Invoke(list, thisArg);
    }

    [JsHostMethod("bind", Length = 1d)]
    private static JsValue Bind(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<IJsCallable>(out var target))
        {
            var realmState = thisValue.TryGetObject<ICallableMetadata>(out var metadata)
                ? metadata.RealmState
                : null;
            throw ThrowTypeError("Function.prototype.bind called on incompatible receiver", realm: realmState);
        }

        var boundThis = args.GetArgument(0);
        var boundArgs = args.SliceFrom(1);
        var targetIsConstructor = JsOps.IsConstructor(thisValue);
        var targetRealmState = thisValue.TryGetObject<ICallableMetadata>(out var callableMetadata)
            ? callableMetadata.RealmState
            : null;
        return (JsValue)HostFunction.CreateBoundFunction(target, boundThis, boundArgs, targetIsConstructor, targetRealmState);
    }

    [JsSymbolMethod("hasInstance", Length = 1d, Writable = false, Configurable = false)]
    private JsValue HasInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<IJsPropertyAccessor>(out _))
        {
            throw ThrowTypeError("Function.prototype[@@hasInstance] called on non-object", realm: Realm);
        }

        var candidate = args.GetArgument(0);
        if (!candidate.TryGetObject<JsObject>(out _) && !candidate.TryGetObject<IJsObjectLike>(out _))
        {
            return new JsValue(false);
        }

        if (!JsOps.TryGetPropertyValueJsValue(thisValue, "prototype", out var protoVal) ||
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

#pragma warning disable CS0618 // Intentional: cursor is IJsPropertyAccessor? from previous call
            cursor = JsOps.GetPrototypePointer(JsValue.FromObjectUnsafe(cursor));
#pragma warning restore CS0618
        }

        return new JsValue(false);
    }

    protected override void ConfigurePrototype()
    {
        // Seed the intrinsic prototype slot before any RealmState-based prototype resolution runs.
        Realm.FunctionPrototype ??= Prototype;

        if (Prototype is IJsPropertyAccessor accessor)
        {
            DefineConstantProperty(accessor, "length", 0d, true);
        }

        AttachArgumentsPoison();
        // [Symbol.hasInstance] is registered via code generation from attribute
    }

    private void AttachArgumentsPoison()
    {
        // ES spec requires "caller" and "arguments" to be "poison pill" accessors
        // on Function.prototype that throw TypeError when accessed.
        // See ECMA-262 AddRestrictedFunctionProperties
        var thrower = new HostFunction((_, _) =>
                throw ThrowTypeError(
                    "'caller' and 'arguments' are restricted function properties and cannot be accessed in this context.",
                    realm: Realm), Realm,
            false);
        var poisonDescriptor = new PropertyDescriptor
        {
            Get = thrower,
            Set = thrower,
            Enumerable = false,
            Configurable = true
        };
        Prototype.DefineProperty("caller", poisonDescriptor);
        Prototype.DefineProperty("arguments", poisonDescriptor);
    }
}

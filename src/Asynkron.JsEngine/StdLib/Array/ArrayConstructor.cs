#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ArrayHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Array", PrototypeType = typeof(ArrayPrototype), Length = 1d, DisplayName = "Array")]
public sealed partial class ArrayConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    // Static methods registered via code generation

    /* FLAKY */
    [JsConstructorMethod("isArray", Length = 1d)]
    public static JsValue IsArray(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        return new JsValue(ArrayIsArray(args.GetArgument(0),
            realm ?? throw new InvalidOperationException("Realm required")));
    }

    /* FLAKY */
    [JsConstructorMethod("of", Length = 0d)]
    public static JsValue Of(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        return JsValue.FromObjectUnsafe(ArrayOf(thisValue, args, realm));
    }

    /* FLAKY */
    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var array = AllocateArrayInstance(thisValue);
        InitializeArrayLength(array, args);
        return JsValue.FromJsArray(array);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        Realm.ArrayConstructor ??= constructor;
        Realm.ArrayPrototype ??= Prototype;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var targetCtor = Realm.ArrayConstructor ?? constructor;
            IJsCallable newTargetCallable;
            if (newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                newTargetCallable = callable;
            }
            else
            {
                newTargetCallable = targetCtor;
            }

            var proto = ResolveConstructPrototype(newTargetCallable, targetCtor, Realm) ??
                        Prototype;
            var instanceRealm = ResolveInstanceRealm(proto, newTargetCallable);
            var array = new JsArray(instanceRealm);
            if (proto is not null)
            {
                array.SetPrototype(proto);
            }

            InitializeArrayLength(array, args);
            return JsValue.FromJsArray(array);
        });

        // isArray, of, and [Symbol.species] are registered via code generation from attributes
        // from and fromAsync need special handling (capture self for mapper environment propagation)
        AttachFrom(constructor);
        AttachFromAsync(constructor);
    }

    private JsArray AllocateArrayInstance(JsValue thisValue)
    {
        if (thisValue.TryGetObject<JsArray>(out var providedArray))
        {
            return providedArray;
        }

        var instance = new JsArray(Realm);
        if (thisValue.TryGetObject<JsObject>(out var obj) && obj.Prototype is { } providedProto)
        {
            instance.SetPrototype(providedProto);
        }
        else if (Prototype is not null && instance.Prototype is null)
        {
            instance.SetPrototype(Prototype);
        }

        return instance;
    }

    private RealmState ResolveInstanceRealm(object? proto, IJsCallable newTarget)
    {
        if (proto is JsObject { RealmState: { } protoRealm })
        {
            return protoRealm;
        }

        return newTarget switch
        {
            HostFunction { RealmState: { } hostRealm } => hostRealm,
            TypedAstEvaluator.TypedFunction { RealmState: { } tfRealm } => tfRealm,
            _ => Realm
        };
    }

    private void InitializeArrayLength(JsArray array, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            array.SetProperty("length", 0d);
            return;
        }

        if (args.Count == 1 && IsNumericPrimitive(args[0]))
        {
            var lengthNumber = JsOps.ToNumber(args[0]);
            if (double.IsNaN(lengthNumber) || double.IsInfinity(lengthNumber) || lengthNumber < 0)
            {
                throw ThrowRangeError("Invalid array length", realm: Realm);
            }

            if (Math.Floor(lengthNumber) != lengthNumber)
            {
                throw ThrowRangeError("Invalid array length", realm: Realm);
            }

            if (lengthNumber > MaxConcreteArrayLength)
            {
                throw ThrowRangeError("Invalid array length", realm: Realm);
            }

            array.SetProperty("length", lengthNumber);
            return;
        }

        array.SetProperty("length", (double)args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            array.SetProperty(ToIndexString(i), args[i]);
        }
    }

    private static bool IsNumericPrimitive(JsValue value)
    {
        return value.IsNumber;
    }

    /* FLAKY */
    private void AttachFrom(HostFunction constructor)
    {
        HostFunction arrayFrom = null!;
        arrayFrom = new HostFunction((thisValue, args) =>
        {
            var result = ArrayFrom(arrayFrom, thisValue, args, Realm);
            return JsValue.FromObjectUnsafe(result);
        }, Realm, false);
        AttachBuiltinMetadata(arrayFrom, "from", 1d);
        arrayFrom.Delete("prototype");
        constructor.DefineProperty("from",
            new PropertyDescriptor { Value = arrayFrom, Writable = true, Enumerable = false, Configurable = true });
    }

    /* FLAKY */
    private void AttachFromAsync(HostFunction constructor)
    {
        HostFunction arrayFromAsync = null!;
        arrayFromAsync = new HostFunction((thisValue, args) =>
        {
            var result = ArrayFromAsync(arrayFromAsync, thisValue, args, Realm);
            return JsValue.FromObjectUnsafe(result);
        }, Realm, false);
        AttachBuiltinMetadata(arrayFromAsync, "fromAsync", 1d);
        arrayFromAsync.Delete("prototype");
        constructor.DefineProperty("fromAsync",
            new PropertyDescriptor
            {
                Value = arrayFromAsync, Writable = true, Enumerable = false, Configurable = true
            });
    }
}

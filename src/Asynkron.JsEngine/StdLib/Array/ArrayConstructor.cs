using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Array", PrototypeType = typeof(ArrayPrototype), Length = 1d, DisplayName = "Array")]
public sealed partial class ArrayConstructor : JsConstructor
{
    public ArrayConstructor(JsObject prototype, RealmState realm) : base(prototype, realm)
    {
    }

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var array = AllocateArrayInstance(thisValue);
        InitializeArrayLength(array, args);
        return array;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        Realm.ArrayConstructor ??= constructor;
        Realm.ArrayPrototype ??= Prototype;

        AttachIsArray(constructor);
        AttachFrom(constructor);
        AttachFromAsync(constructor);
        AttachOf(constructor);
        AttachSpeciesGetter(constructor);
    }

    private JsArray AllocateArrayInstance(object? thisValue)
    {
        if (thisValue is JsArray providedArray)
        {
            return providedArray;
        }

        var instance = new JsArray(Realm);
        if (thisValue is JsObject { Prototype: JsObject providedProto })
        {
            instance.SetPrototype(providedProto);
        }
        else if (Prototype is not null && instance.Prototype is null)
        {
            instance.SetPrototype(Prototype);
        }

        return instance;
    }

    private void InitializeArrayLength(JsArray array, IReadOnlyList<object?> args)
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
                throw StandardLibrary.ThrowRangeError("Invalid array length", realm: Realm);
            }

            if (Math.Floor(lengthNumber) != lengthNumber)
            {
                throw StandardLibrary.ThrowRangeError("Invalid array length", realm: Realm);
            }

            if (lengthNumber > StandardLibrary.MaxConcreteArrayLength)
            {
                throw StandardLibrary.ThrowRangeError("Invalid array length", realm: Realm);
            }

            array.SetProperty("length", lengthNumber);
            return;
        }

        array.SetProperty("length", (double)args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            array.SetProperty(StandardLibrary.ToIndexString(i), args[i]);
        }
    }

    private static bool IsNumericPrimitive(object? value)
    {
        return value is double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte;
    }

    private void AttachIsArray(HostFunction constructor)
    {
        var isArray = new HostFunction(args => StandardLibrary.ArrayIsArray(args.GetArgument(0), Realm), Realm,
            isConstructor: false);
        StandardLibrary.AttachBuiltinMetadata(isArray, "isArray", 1d);
        isArray.Delete("prototype");
        constructor.DefineProperty("isArray",
            new PropertyDescriptor
            {
                Value = isArray, Writable = true, Enumerable = false, Configurable = true
            });
    }

    private void AttachFrom(HostFunction constructor)
    {
        HostFunction arrayFrom = null!;
        arrayFrom = new HostFunction((thisValue, args) => StandardLibrary.ArrayFrom(arrayFrom, thisValue, args, Realm),
            Realm, isConstructor: false);
        StandardLibrary.AttachBuiltinMetadata(arrayFrom, "from", 1d);
        arrayFrom.Delete("prototype");
        constructor.DefineProperty("from",
            new PropertyDescriptor
            {
                Value = arrayFrom, Writable = true, Enumerable = false, Configurable = true
            });
    }

    private void AttachFromAsync(HostFunction constructor)
    {
        HostFunction arrayFromAsync = null!;
        arrayFromAsync = new HostFunction(
            (thisValue, args) => StandardLibrary.ArrayFromAsync(arrayFromAsync, thisValue, args, Realm),
            Realm, isConstructor: false);
        StandardLibrary.AttachBuiltinMetadata(arrayFromAsync, "fromAsync", 1d);
        arrayFromAsync.Delete("prototype");
        constructor.DefineProperty("fromAsync",
            new PropertyDescriptor
            {
                Value = arrayFromAsync, Writable = true, Enumerable = false, Configurable = true
            });
    }

    private void AttachOf(HostFunction constructor)
    {
        HostFunction arrayOf = null!;
        arrayOf = new HostFunction((thisValue, args) => StandardLibrary.ArrayOf(arrayOf, thisValue, args, Realm), Realm,
            isConstructor: false);
        StandardLibrary.AttachBuiltinMetadata(arrayOf, "of", 0d);
        arrayOf.Delete("prototype");
        constructor.DefineProperty("of",
            new PropertyDescriptor
            {
                Value = arrayOf, Writable = true, Enumerable = false, Configurable = true
            });
    }

    private void AttachSpeciesGetter(HostFunction constructor)
    {
        var speciesGetter = new HostFunction((thisValue, _) => thisValue, Realm, isConstructor: false);
        speciesGetter.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "get [Symbol.species]",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        speciesGetter.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

        constructor.DefineProperty(StandardLibrary.SymbolSpeciesKey,
            new PropertyDescriptor
            {
                Get = speciesGetter,
                Enumerable = false,
                Configurable = true
            });
    }
}

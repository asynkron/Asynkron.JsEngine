using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("TypedArray", ToStringTag = "TypedArray")]
public sealed partial class TypedArrayPrototype : JsPrototype
{
    [JsHostMethod("reduce", Length = 1d)]
    public JsValue Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayReduceInternal(thisValue.ToObject(), args, Realm, "%TypedArray%.prototype.reduce", false));
    }

    [JsHostMethod("reduceRight", Length = 1d)]
    public JsValue ReduceRight(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayReduceInternal(thisValue.ToObject(), args, Realm, "%TypedArray%.prototype.reduceRight", true));
    }

    [JsHostMethod("map", Length = 1d)]
    public JsValue Map(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayMapInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("filter", Length = 1d)]
    public JsValue Filter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayFilterInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("every", Length = 1d)]
    public JsValue Every(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayEveryInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("find", Length = 1d)]
    public JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayFindInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("findIndex", Length = 1d)]
    public JsValue FindIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayFindIndexInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("findLast", Length = 1d)]
    public JsValue FindLast(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayFindLastInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("findLastIndex", Length = 1d)]
    public JsValue FindLastIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayFindLastIndexInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayForEachInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("fill", Length = 1d)]
    public JsValue Fill(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayFillInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("copyWithin", Length = 2d)]
    public JsValue CopyWithin(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayCopyWithinInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("reverse", Length = 0d)]
    public JsValue Reverse(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayReverseInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("toReversed", Length = 0d)]
    public JsValue ToReversed(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayToReversedInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("toSorted", Length = 1d)]
    public JsValue ToSorted(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayToSortedInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("toSpliced", Length = 2d)]
    public JsValue ToSpliced(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayToSplicedInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("with", Length = 2d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayWithInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("indexOf", Length = 1d)]
    public JsValue IndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayIndexOfInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("lastIndexOf", Length = 1d)]
    public JsValue LastIndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayLastIndexOfInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("includes", Length = 1d)]
    public JsValue Includes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(TypedArrayIncludesInternal(thisValue.ToObject(), args, Realm));
    }

    [JsHostMethod("some", Length = 1d)]
    public JsValue Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(SomeLike(thisValue, args.ToList(), Realm, "%TypedArray%.prototype.some"));
    }

    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateTypedArrayReceiverInternal(thisValue, "%TypedArray%.prototype.values", Realm);
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray, idx => typedArray.GetValueForIndex((int)idx), Realm));
    }

    [JsHostMethod("keys", Length = 0d)]
    public JsValue Keys(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateTypedArrayReceiverInternal(thisValue, "%TypedArray%.prototype.keys", Realm);
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray, idx => new JsValue((double)idx), Realm));
    }

    [JsHostMethod("entries", Length = 0d)]
    public JsValue Entries(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateTypedArrayReceiverInternal(thisValue, "%TypedArray%.prototype.entries", Realm);
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(
            typedArray,
            idx =>
            {
                var pair = new JsArray(Realm);
                pair.Push((double)idx);
                pair.Push(typedArray.GetValueForIndex((int)idx));
                return JsValue.FromObjectUnsafe(pair);
            },
            Realm));
    }

    protected override void ConfigurePrototype()
    {
        Realm.TypedArrayPrototype ??= Prototype as JsObject;

        // Set up Symbol.iterator to point to values
        if (Prototype.TryGetProperty("values", out var valuesMethod))
        {
            Prototype.DefineProperty(SymbolKeys.Iterator,
                new PropertyDescriptor
                {
                    Value = valuesMethod,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });
        }
    }
}

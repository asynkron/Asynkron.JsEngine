using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateMapConstructor()
    {
        var mapPrototype = new JsObject();
        HostFunction mapConstructor = null!;

        mapConstructor = new HostFunction((_, args) => MapCtorCore(args, mapConstructor), isConstructor: true);
        mapConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
            MapCtorCore(args, newTarget as IJsCallable ?? mapConstructor));

        AddMapMethods(mapPrototype);

        // Add size getter to prototype
        mapPrototype.DefineProperty("size",
            new PropertyDescriptor
            {
                Get = new HostFunction((thisVal, _) =>
                {
                    var map = RequireMap(thisVal);
                    return map is not null ? (double)map.Size : 0d;
                }),
                Enumerable = false,
                Configurable = true
            });

        mapConstructor.SetProperty("prototype", mapPrototype);
        mapPrototype.SetProperty("constructor", mapConstructor);

        return mapConstructor;

        object? MapCtorCore(IReadOnlyList<object?> args, IJsCallable newTarget)
        {
            var map = new JsMap();
            map.SetPrototype(mapPrototype);

            // If an iterable is provided, populate the map
            if (args.Count > 0 && args[0] is not null && args[0] is not Symbol)
            {
                var iterable = args[0];
                if (iterable is JsArray entries)
                {
                    foreach (var entry in entries.Items)
                    {
                        // For each entry, get [0] (key) and [1] (value)
                        object? key;
                        object? value;
                        if (entry is JsArray pair)
                        {
                            key = pair.GetElement(0);
                            value = pair.GetElement(1);
                        }
                        else if (entry is IJsPropertyAccessor accessor)
                        {
                            accessor.TryGetProperty("0", out key);
                            accessor.TryGetProperty("1", out value);
                        }
                        else
                        {
                            // Entry is not a valid iterable-like
                            continue;
                        }

                        map.Set(key, value);
                    }
                }
            }

            // If newTarget is not the Map constructor, wrap in JsObject with proper prototype
            if (!ReferenceEquals(newTarget, mapConstructor))
            {
                var instance = new JsObject();
                if (newTarget is IJsPropertyAccessor accessor &&
                    accessor.TryGetProperty("prototype", out var proto) &&
                    proto is IJsObjectLike protoObj)
                {
                    instance.SetPrototype(protoObj);
                }
                else
                {
                    instance.SetPrototype(mapPrototype);
                }

                instance.SetProperty("_internalMap", map);
                return instance;
            }

            return map;
        }
    }

    private static JsMap? RequireMap(object? thisVal)
    {
        if (thisVal is JsMap directMap)
        {
            return directMap;
        }

        if (thisVal is JsObject obj)
        {
            var descriptor = obj.GetOwnPropertyDescriptor("_internalMap");
            if (descriptor?.Value is JsMap internalMap)
            {
                return internalMap;
            }
        }

        return null;
    }

    /// <summary>
    ///     Adds instance methods to a Map object.
    /// </summary>
    private static void AddMapMethods(IJsPropertyAccessor target)
    {
        // Note: size needs special handling as a getter - for now we'll just access it dynamically in the methods
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        target.SetHostedProperty("set", MapSet_Set);
        target.SetHostedProperty("get", MapSet_Get);
        target.SetHostedProperty("has", MapSet_Has);
        target.SetHostedProperty("delete", MapSet_Delete);
        target.SetHostedProperty("clear", MapSet_Clear);
        target.SetHostedProperty("forEach", MapSet_ForEach);
        target.SetHostedProperty("entries", MapSet_Entries);
        target.SetHostedProperty("keys", MapSet_Keys);
        target.SetHostedProperty("values", MapSet_Values);
        target.SetHostedProperty(iteratorKey, MapSet_Entries);
    }

    /// <summary>
    ///     Creates the Set constructor function.
    /// </summary>
    public static IJsCallable CreateSetConstructor()
    {
        var setPrototype = new JsObject();
        HostFunction setConstructor = null!;

        setConstructor = new HostFunction((_, args) => SetCtorCore(args, setConstructor), isConstructor: true);
        setConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
            SetCtorCore(args, newTarget as IJsCallable ?? setConstructor));

        AddSetMethods(setPrototype);

        // Add size getter to prototype
        setPrototype.DefineProperty("size",
            new PropertyDescriptor
            {
                Get = new HostFunction((thisVal, _) =>
                {
                    var set = RequireSet(thisVal);
                    return set is not null ? (double)set.Size : 0d;
                }),
                Enumerable = false,
                Configurable = true
            });

        setConstructor.SetProperty("prototype", setPrototype);
        setPrototype.SetProperty("constructor", setConstructor);

        return setConstructor;

        object? SetCtorCore(IReadOnlyList<object?> args, IJsCallable newTarget)
        {
            var set = new JsSet();
            set.SetPrototype(setPrototype);

            // If an iterable is provided, populate the set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    set.Add(value);
                }
            }

            // If newTarget is not the Set constructor, wrap in JsObject with proper prototype
            if (!ReferenceEquals(newTarget, setConstructor))
            {
                var instance = new JsObject();
                if (newTarget is IJsPropertyAccessor accessor &&
                    accessor.TryGetProperty("prototype", out var proto) &&
                    proto is IJsObjectLike protoObj)
                {
                    instance.SetPrototype(protoObj);
                }
                else
                {
                    instance.SetPrototype(setPrototype);
                }

                instance.SetProperty("_internalSet", set);
                return instance;
            }

            return set;
        }
    }

    private static JsSet? RequireSet(object? thisVal)
    {
        if (thisVal is JsSet directSet)
        {
            return directSet;
        }

        if (thisVal is JsObject obj)
        {
            var descriptor = obj.GetOwnPropertyDescriptor("_internalSet");
            if (descriptor?.Value is JsSet internalSet)
            {
                return internalSet;
            }
        }

        return null;
    }

    /// <summary>
    ///     Adds instance methods to a Set object.
    /// </summary>
    private static void AddSetMethods(IJsPropertyAccessor target)
    {
        // Note: size needs special handling as a getter - handled in Evaluator.TryGetPropertyValue
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        target.SetHostedProperty("add", Set_Add);

        target.SetHostedProperty("has", Set_Has);

        target.SetHostedProperty("delete", Set_Delete);
        target.SetHostedProperty("clear", Set_Clear);

        // forEach(callback, thisArg)
        target.SetHostedProperty("forEach", Set_ForEach);

        // entries()
        target.SetHostedProperty("entries", Set_Entries);

        // keys()
        target.SetHostedProperty("keys", Set_Keys);

        // values()
        target.SetHostedProperty("values", Set_Values);

        target.SetHostedProperty(iteratorKey, Set_Values);
    }

    /// <summary>
    ///     Creates the WeakMap constructor function.
    /// </summary>
    public static IJsCallable CreateWeakMapConstructor()
    {
        var weakMapPrototype = new JsObject();
        AddWeakMapMethods(weakMapPrototype);

        var weakMapConstructor = new HostFunction((thisValue, args) =>
        {
            var weakMap = thisValue as JsWeakMap ?? new JsWeakMap();

            if (thisValue is JsObject { Prototype: { } providedProto })
            {
                weakMap.SetPrototype(providedProto);
            }
            else if (weakMap.Prototype is null)
            {
                weakMap.SetPrototype(weakMapPrototype);
            }

            // Note: WeakMap constructor can accept an iterable, but we'll start with basic support
            // If an iterable is provided, populate the weak map
            if (args.Count > 0 && args[0] is JsArray entries)
            {
                foreach (var entry in entries.Items)
                {
                    if (entry is JsArray { Items.Count: >= 2 } pair)
                    {
                        try
                        {
                            weakMap.Set(pair.GetElement(0), pair.GetElement(1));
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(ex.Message);
                        }
                    }
                }
            }

            return weakMap;
        }, isConstructor: true);

        weakMapConstructor.SetProperty("prototype", weakMapPrototype);
        weakMapPrototype.SetProperty("constructor", weakMapConstructor);

        return weakMapConstructor;
    }

    /// <summary>
    ///     Adds instance methods to a WeakMap object.
    /// </summary>
    private static void AddWeakMapMethods(IJsPropertyAccessor target)
    {
        target.SetHostedProperty("set", WeakMap_Set);
        target.SetHostedProperty("get", WeakMap_Get);
        target.SetHostedProperty("has", WeakMap_Has);
        target.SetHostedProperty("delete", WeakMap_Delete);
    }

    /// <summary>
    ///     Creates the WeakSet constructor function.
    /// </summary>
    public static IJsCallable CreateWeakSetConstructor()
    {
        var weakSetPrototype = new JsObject();
        AddWeakSetMethods(weakSetPrototype);

        var weakSetConstructor = new HostFunction((thisValue, args) =>
        {
            var weakSet = thisValue as JsWeakSet ?? new JsWeakSet();

            if (thisValue is JsObject { Prototype: { } providedProto })
            {
                weakSet.SetPrototype(providedProto);
            }
            else if (weakSet.Prototype is null)
            {
                weakSet.SetPrototype(weakSetPrototype);
            }

            // If an iterable is provided, populate the weak set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    try
                    {
                        weakSet.Add(value);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message);
                    }
                }
            }

            return weakSet;
        }, isConstructor: true);

        weakSetConstructor.SetProperty("prototype", weakSetPrototype);
        weakSetPrototype.SetProperty("constructor", weakSetConstructor);

        return weakSetConstructor;
    }

    /// <summary>
    ///     Adds instance methods to a WeakSet object.
    /// </summary>
    private static void AddWeakSetMethods(IJsPropertyAccessor target)
    {
        target.SetHostedProperty("add", WeakSet_Add);
        target.SetHostedProperty("has", WeakSet_Has);
        target.SetHostedProperty("delete", WeakSet_Delete);
    }

    private static object? MapSet_Set(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return Symbol.Undefined;
        }

        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        map.Set(key, value);
        return thisValue; // Return the original this value for chaining
    }

    private static object? MapSet_Get(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return Symbol.Undefined;
        }

        var key = args.GetArgument(0);
        return map.Get(key);
    }

    private static object MapSet_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return false;
        }

        var key = args.GetArgument(0);
        return map.Has(key);
    }

    private static object MapSet_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return false;
        }

        var key = args.GetArgument(0);
        return map.Delete(key);
    }

    private static object? MapSet_Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        map?.Clear();

        return Symbol.Undefined;
    }

    private static object? MapSet_ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return Symbol.Undefined;
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return Symbol.Undefined;
        }

        var thisArg = args.Count > 1 ? args[1] : null;
        map.ForEach(callback, thisArg);
        return Symbol.Undefined;
    }

    private static object? MapSet_Entries(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return Symbol.Undefined;
        }

        return CreateMapIterator(map, MapIterationKind.Entries);
    }

    private static object? MapSet_Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return Symbol.Undefined;
        }

        return CreateMapIterator(map, MapIterationKind.Keys);
    }

    private static object? MapSet_Values(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        if (map is null)
        {
            return Symbol.Undefined;
        }

        return CreateMapIterator(map, MapIterationKind.Values);
    }

    private static object? Set_Add(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return Symbol.Undefined;
        }

        var value = args.GetArgument(0);
        set.Add(value);
        return thisValue; // Return the original this value for chaining
    }

    private static object Set_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return false;
        }

        var value = args.GetArgument(0);
        return set.Has(value);
    }

    private static object Set_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return false;
        }

        var value = args.GetArgument(0);
        return set.Delete(value);
    }

    private static object? Set_Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        var set = RequireSet(thisValue);
        set?.Clear();

        return Symbol.Undefined;
    }

    private static object? Set_ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return Symbol.Undefined;
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return Symbol.Undefined;
        }

        var thisArg = args.Count > 1 ? args[1] : null;
        set.ForEach(callback, thisArg);
        return Symbol.Undefined;
    }

    private static object? Set_Entries(object? thisValue, IReadOnlyList<object?> _)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return Symbol.Undefined;
        }

        return CreateSetIterator(set, SetIterationKind.Entries);
    }

    private static object? Set_Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return Symbol.Undefined;
        }

        return CreateSetIterator(set, SetIterationKind.Keys);
    }

    private static object? Set_Values(object? thisValue, IReadOnlyList<object?> _)
    {
        var set = RequireSet(thisValue);
        if (set is null)
        {
            return Symbol.Undefined;
        }

        return CreateSetIterator(set, SetIterationKind.Values);
    }

    private enum MapIterationKind
    {
        Entries,
        Keys,
        Values
    }

    private static JsObject CreateMapIterator(JsMap map, MapIterationKind kind)
    {
        var iterator = new JsObject();
        var index = 0;

        iterator.SetHostedProperty("next", (_, _) =>
        {
            var result = new JsObject();
            if (index < map.EntryCount)
            {
                var entry = map.GetEntry(index++);
                var value = kind switch
                {
                    MapIterationKind.Keys => entry.Key,
                    MapIterationKind.Values => entry.Value,
                    _ => CreateEntryPair(entry.Key, entry.Value)
                };

                result.SetProperty("value", value);
                result.SetProperty("done", false);
            }
            else
            {
                result.SetProperty("value", Symbol.Undefined);
                result.SetProperty("done", true);
            }

            return result;
        });

        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
        iterator.SetHostedProperty(iteratorKey, (_, _) => iterator);
        return iterator;
    }

    private enum SetIterationKind
    {
        Entries,
        Keys,
        Values
    }

    private static JsObject CreateSetIterator(JsSet set, SetIterationKind kind)
    {
        var iterator = new JsObject();
        var index = 0;

        iterator.SetHostedProperty("next", (_, _) =>
        {
            var result = new JsObject();
            if (index < set.ValueCount)
            {
                var current = set.GetValue(index++);
                var value = kind switch
                {
                    SetIterationKind.Entries => CreateEntryPair(current, current),
                    _ => current
                };

                result.SetProperty("value", value);
                result.SetProperty("done", false);
            }
            else
            {
                result.SetProperty("value", Symbol.Undefined);
                result.SetProperty("done", true);
            }

            return result;
        });

        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
        iterator.SetHostedProperty(iteratorKey, (_, _) => iterator);
        return iterator;
    }

    private static JsArray CreateEntryPair(object? first, object? second)
    {
        var pair = new JsArray();
        pair.SetElement(0, first);
        pair.SetElement(1, second);
        return pair;
    }

    private static object? WeakMap_Set(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return Symbol.Undefined;
        }

        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        try
        {
            return weakMap.Set(key, value);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    private static object? WeakMap_Get(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return Symbol.Undefined;
        }

        var key = args.GetArgument(0);
        return weakMap.Get(key);
    }

    private static object WeakMap_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return false;
        }

        var key = args.GetArgument(0);
        return weakMap.Has(key);
    }

    private static object WeakMap_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return false;
        }

        var key = args.GetArgument(0);
        return weakMap.Delete(key);
    }

    private static object? WeakSet_Add(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakSet weakSet)
        {
            return Symbol.Undefined;
        }

        var value = args.GetArgument(0);
        try
        {
            return weakSet.Add(value);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    private static object WeakSet_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakSet weakSet)
        {
            return false;
        }

        var value = args.GetArgument(0);
        return weakSet.Has(value);
    }

    private static object WeakSet_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakSet weakSet)
        {
            return false;
        }

        var value = args.GetArgument(0);
        return weakSet.Delete(value);
    }
}

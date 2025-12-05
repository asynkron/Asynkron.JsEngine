using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateMapConstructor()
    {
        var mapConstructor = new HostFunction(args =>
        {
            var map = new JsMap();

            // If an iterable is provided, populate the map
            if (args.Count > 0 && args[0] is JsArray entries)
            {
                foreach (var entry in entries.Items)
                {
                    if (entry is JsArray { Items.Count: >= 2 } pair)
                    {
                        map.Set(pair.GetElement(0), pair.GetElement(1));
                    }
                }
            }

            AddMapMethods(map);
            return map;
        });

        return mapConstructor;
    }

    /// <summary>
    ///     Adds instance methods to a Map object.
    /// </summary>
    private static void AddMapMethods(JsMap map)
    {
        // Note: size needs special handling as a getter - for now we'll just access it dynamically in the methods
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        map.SetHostedProperty("set", MapSet_Set);
        map.SetHostedProperty("get", MapSet_Get);
        map.SetHostedProperty("has", MapSet_Has);
        map.SetHostedProperty("delete", MapSet_Delete);
        map.SetHostedProperty("clear", MapSet_Clear);
        map.SetHostedProperty("forEach", MapSet_ForEach);
        map.SetHostedProperty("entries", MapSet_Entries);
        map.SetHostedProperty("keys", MapSet_Keys);
        map.SetHostedProperty("values", MapSet_Values);
        map.SetHostedProperty(iteratorKey, MapSet_Entries);
    }

    /// <summary>
    ///     Creates the Set constructor function.
    /// </summary>
    public static IJsCallable CreateSetConstructor()
    {
        var setConstructor = new HostFunction(args =>
        {
            var set = new JsSet();

            // If an iterable is provided, populate the set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    set.Add(value);
                }
            }

            AddSetMethods(set);
            return set;
        });

        return setConstructor;
    }

    /// <summary>
    ///     Adds instance methods to a Set object.
    /// </summary>
    private static void AddSetMethods(JsSet set)
    {
        // Note: size needs special handling as a getter - handled in Evaluator.TryGetPropertyValue
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        set.SetHostedProperty("add", Set_Add);

        set.SetHostedProperty("has", Set_Has);

        set.SetHostedProperty("delete", Set_Delete);
        set.SetHostedProperty("clear", Set_Clear);

        // forEach(callback, thisArg)
        set.SetHostedProperty("forEach", Set_ForEach);

        // entries()
        set.SetHostedProperty("entries", Set_Entries);

        // keys()
        set.SetHostedProperty("keys", Set_Keys);

        // values()
        set.SetHostedProperty("values", Set_Values);

        set.SetHostedProperty(iteratorKey, Set_Values);
    }

    /// <summary>
    ///     Creates the WeakMap constructor function.
    /// </summary>
    public static IJsCallable CreateWeakMapConstructor()
    {
        var weakMapConstructor = new HostFunction(args =>
        {
            var weakMap = new JsWeakMap();

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

            AddWeakMapMethods(weakMap);
            return weakMap;
        });

        return weakMapConstructor;
    }

    /// <summary>
    ///     Adds instance methods to a WeakMap object.
    /// </summary>
    private static void AddWeakMapMethods(JsWeakMap weakMap)
    {
        weakMap.SetHostedProperty("set", WeakMap_Set);
        weakMap.SetHostedProperty("get", WeakMap_Get);
        weakMap.SetHostedProperty("has", WeakMap_Has);
        weakMap.SetHostedProperty("delete", WeakMap_Delete);
    }

    /// <summary>
    ///     Creates the WeakSet constructor function.
    /// </summary>
    public static IJsCallable CreateWeakSetConstructor()
    {
        var weakSetConstructor = new HostFunction(args =>
        {
            var weakSet = new JsWeakSet();

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

            AddWeakSetMethods(weakSet);
            return weakSet;
        });

        return weakSetConstructor;
    }

    /// <summary>
    ///     Adds instance methods to a WeakSet object.
    /// </summary>
    private static void AddWeakSetMethods(JsWeakSet weakSet)
    {
        weakSet.SetHostedProperty("add", WeakSet_Add);
        weakSet.SetHostedProperty("has", WeakSet_Has);
        weakSet.SetHostedProperty("delete", WeakSet_Delete);
    }

    private static object? MapSet_Set(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return Symbol.Undefined;
        }

        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        return map.Set(key, value);
    }

    private static object? MapSet_Get(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return Symbol.Undefined;
        }

        var key = args.GetArgument(0);
        return map.Get(key);
    }

    private static object MapSet_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return false;
        }

        var key = args.GetArgument(0);
        return map.Has(key);
    }

    private static object MapSet_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return false;
        }

        var key = args.GetArgument(0);
        return map.Delete(key);
    }

    private static object? MapSet_Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is JsMap map)
        {
            map.Clear();
        }

        return Symbol.Undefined;
    }

    private static object? MapSet_ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
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
        if (thisValue is not JsMap map)
        {
            return Symbol.Undefined;
        }

        return CreateMapIterator(map, MapIterationKind.Entries);
    }

    private static object? MapSet_Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is not JsMap map)
        {
            return Symbol.Undefined;
        }

        return CreateMapIterator(map, MapIterationKind.Keys);
    }

    private static object? MapSet_Values(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is not JsMap map)
        {
            return Symbol.Undefined;
        }

        return CreateMapIterator(map, MapIterationKind.Values);
    }

    private static object? Set_Add(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return Symbol.Undefined;
        }

        var value = args.GetArgument(0);
        return set.Add(value);
    }

    private static object Set_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return false;
        }

        var value = args.GetArgument(0);
        return set.Has(value);
    }

    private static object Set_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return false;
        }

        var value = args.GetArgument(0);
        return set.Delete(value);
    }

    private static object? Set_Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is JsSet set)
        {
            set.Clear();
        }

        return Symbol.Undefined;
    }

    private static object? Set_ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
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
        if (thisValue is not JsSet set)
        {
            return Symbol.Undefined;
        }

        return CreateSetIterator(set, SetIterationKind.Entries);
    }

    private static object? Set_Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is not JsSet set)
        {
            return Symbol.Undefined;
        }

        return CreateSetIterator(set, SetIterationKind.Keys);
    }

    private static object? Set_Values(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is not JsSet set)
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

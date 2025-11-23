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

        map.SetHostedProperty("set", MapSet_Set);
        map.SetHostedProperty("get", MapSet_Get);
        map.SetHostedProperty("has", MapSet_Has);
        map.SetHostedProperty("delete", MapSet_Delete);
        map.SetHostedProperty("clear", MapSet_Clear);
        map.SetHostedProperty("forEach", MapSet_ForEach);
        map.SetHostedProperty("entries", MapSet_Entries);
        map.SetHostedProperty("keys", MapSet_Keys);
        map.SetHostedProperty("values", MapSet_Values);
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
            return Symbols.Undefined;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        var value = args.Count > 1 ? args[1] : Symbols.Undefined;
        return map.Set(key, value);
    }

    private static object? MapSet_Get(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return Symbols.Undefined;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        return map.Get(key);
    }

    private static object MapSet_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return false;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        return map.Has(key);
    }

    private static object MapSet_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return false;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        return map.Delete(key);
    }

    private static object? MapSet_Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is JsMap map)
        {
            map.Clear();
        }

        return Symbols.Undefined;
    }

    private static object? MapSet_ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsMap map)
        {
            return Symbols.Undefined;
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return Symbols.Undefined;
        }

        var thisArg = args.Count > 1 ? args[1] : null;
        map.ForEach(callback, thisArg);
        return Symbols.Undefined;
    }

    private static object? MapSet_Entries(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsMap map ? map.Entries() : Symbols.Undefined;
    }

    private static object? MapSet_Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsMap map ? map.Keys() : Symbols.Undefined;
    }

    private static object? MapSet_Values(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsMap map ? map.Values() : Symbols.Undefined;
    }

    private static object? Set_Add(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return Symbols.Undefined;
        }

        var value = args.Count > 0 ? args[0] : Symbols.Undefined;
        return set.Add(value);
    }

    private static object Set_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return false;
        }

        var value = args.Count > 0 ? args[0] : Symbols.Undefined;
        return set.Has(value);
    }

    private static object Set_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return false;
        }

        var value = args.Count > 0 ? args[0] : Symbols.Undefined;
        return set.Delete(value);
    }

    private static object? Set_Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is JsSet set)
        {
            set.Clear();
        }

        return Symbols.Undefined;
    }

    private static object? Set_ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsSet set)
        {
            return Symbols.Undefined;
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return Symbols.Undefined;
        }

        var thisArg = args.Count > 1 ? args[1] : null;
        set.ForEach(callback, thisArg);
        return Symbols.Undefined;
    }

    private static object? Set_Entries(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsSet set ? set.Entries() : Symbols.Undefined;
    }

    private static object? Set_Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsSet set ? set.Keys() : Symbols.Undefined;
    }

    private static object? Set_Values(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsSet set ? set.Values() : Symbols.Undefined;
    }

    private static object? WeakMap_Set(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return Symbols.Undefined;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        var value = args.Count > 1 ? args[1] : Symbols.Undefined;
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
            return Symbols.Undefined;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        return weakMap.Get(key);
    }

    private static object WeakMap_Has(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return false;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        return weakMap.Has(key);
    }

    private static object WeakMap_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakMap weakMap)
        {
            return false;
        }

        var key = args.Count > 0 ? args[0] : Symbols.Undefined;
        return weakMap.Delete(key);
    }

    private static object? WeakSet_Add(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakSet weakSet)
        {
            return Symbols.Undefined;
        }

        var value = args.Count > 0 ? args[0] : Symbols.Undefined;
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

        var value = args.Count > 0 ? args[0] : Symbols.Undefined;
        return weakSet.Has(value);
    }

    private static object WeakSet_Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsWeakSet weakSet)
        {
            return false;
        }

        var value = args.Count > 0 ? args[0] : Symbols.Undefined;
        return weakSet.Delete(value);
    }
}

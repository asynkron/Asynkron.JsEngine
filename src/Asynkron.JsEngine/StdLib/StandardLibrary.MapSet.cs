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

        // set(key, value)
        map.SetProperty("set", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            var value = args.Count > 1 ? args[1] : Symbols.Undefined;
            return m.Set(key, value);
        }));

        // get(key)
        map.SetProperty("get", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return m.Get(key);
        }));

        // has(key)
        map.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return m.Has(key);
        }));

        // delete(key)
        map.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return m.Delete(key);
        }));

        // clear()
        map.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsMap m)
            {
                m.Clear();
            }

            return Symbols.Undefined;
        }));

        // forEach(callback, thisArg)
        map.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return Symbols.Undefined;
            }

            var thisArg = args.Count > 1 ? args[1] : null;
            m.ForEach(callback, thisArg);
            return Symbols.Undefined;
        }));

        // entries()
        map.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            return m.Entries();
        }));

        // keys()
        map.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            return m.Keys();
        }));

        // values()
        map.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            return m.Values();
        }));
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

        // add(value)
        set.SetProperty("add", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return s.Add(value);
        }));

        // has(value)
        set.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return s.Has(value);
        }));

        // delete(value)
        set.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return s.Delete(value);
        }));

        // clear()
        set.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsSet s)
            {
                s.Clear();
            }

            return Symbols.Undefined;
        }));

        // forEach(callback, thisArg)
        set.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return Symbols.Undefined;
            }

            var thisArg = args.Count > 1 ? args[1] : null;
            s.ForEach(callback, thisArg);
            return Symbols.Undefined;
        }));

        // entries()
        set.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            return s.Entries();
        }));

        // keys()
        set.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            return s.Keys();
        }));

        // values()
        set.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            return s.Values();
        }));
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
        // set(key, value)
        weakMap.SetProperty("set", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            var value = args.Count > 1 ? args[1] : Symbols.Undefined;
            try
            {
                return wm.Set(key, value);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }));

        // get(key)
        weakMap.SetProperty("get", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return wm.Get(key);
        }));

        // has(key)
        weakMap.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return wm.Has(key);
        }));

        // delete(key)
        weakMap.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return wm.Delete(key);
        }));
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
        // add(value)
        weakSet.SetProperty("add", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws)
            {
                return Symbols.Undefined;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            try
            {
                return ws.Add(value);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }));

        // has(value)
        weakSet.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return ws.Has(value);
        }));

        // delete(value)
        weakSet.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return ws.Delete(value);
        }));
    }
}

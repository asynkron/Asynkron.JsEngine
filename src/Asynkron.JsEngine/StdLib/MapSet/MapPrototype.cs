#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Map", ToStringTag = "Map", InstanceType = typeof(JsMap))]
[JsSymbolAlias("iterator", "entries")]
public sealed partial class MapPrototype
{
    /* FLAKY */
    [JsHostMethod("set", Length = 2d)]
    public JsValue Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        map.Set(args.GetArgument(0), args.GetArgument(1));
        return thisValue;
    }

    /* FLAKY */
    [JsHostMethod("get", Length = 1d)]
    public JsValue Get(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return map.Get(args.GetArgument(0));
    }

    /* FLAKY */
    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(map.Has(args.GetArgument(0)));
    }

    /* FLAKY */
    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(map.Delete(args.GetArgument(0)));
    }

    /* FLAKY */
    [JsHostMethod("clear", Length = 0d)]
    public JsValue Clear(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        map.Clear();
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        if (!args.GetArgument(0).TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("Map.prototype.forEach callback must be callable", realm: Realm);
        }

        map.ForEach(callback, args.GetArgument(1));
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("entries", Length = 0d)]
    public JsValue Entries(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(CreateMapIterator(map, MapIterationKind.Entries));
    }

    /* FLAKY */
    [JsHostMethod("keys", Length = 0d)]
    public JsValue Keys(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(CreateMapIterator(map, MapIterationKind.Keys));
    }

    /* FLAKY */
    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(CreateMapIterator(map, MapIterationKind.Values));
    }

    /* FLAKY */
    [JsHostGetter("size")]
    public JsValue Size(JsValue thisValue)
    {
        var map = RequireInstance(thisValue);
        return new JsValue((double)map.Size);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.MapPrototype ??= Prototype as JsObject;

        // [Symbol.iterator] is registered via code generation from [JsSymbolAlias] attribute
    }

    private JsObject CreateMapIterator(JsMap map, MapIterationKind kind)
    {
        var iterator = new JsObject { RealmState = Realm };
        var index = 0;

        iterator.SetHostedProperty("next", (_, _) =>
        {
            var result = new JsObject { RealmState = Realm };
            if (index < map.EntryCount)
            {
                var entry = map.GetEntry(index++);
                var value = kind switch
                {
                    MapIterationKind.Keys => entry.Key,
                    MapIterationKind.Values => entry.Value,
                    _ => CreateEntryPair(entry.Key, entry.Value)
                };

                result.SetProperty("value", JsValue.FromObjectUnsafe(value));
                result.SetProperty("done", false);
            }
            else
            {
                result.SetProperty("value", Symbol.Undefined);
                result.SetProperty("done", true);
            }

            return result;
        });

        var iteratorKey = SymbolKeys.Iterator;
        iterator.SetHostedProperty(iteratorKey, (_, _) => iterator);
        return iterator;
    }

    private JsArray CreateEntryPair(object? first, object? second)
    {
        var pair = new JsArray(Realm);
        pair.SetElement(0, first);
        pair.SetElement(1, second);
        return pair;
    }

    private enum MapIterationKind
    {
        Entries,
        Keys,
        Values
    }
}

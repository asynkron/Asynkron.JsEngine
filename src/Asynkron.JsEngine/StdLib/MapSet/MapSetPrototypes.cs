using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Map", ToStringTag = "Map")]
public sealed partial class MapPrototype
{
    private enum MapIterationKind
    {
        Entries,
        Keys,
        Values
    }

    [JsHostMethod("set", Length = 2d)]
    public object? Set(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        map.Set(args.GetArgument(0), args.GetArgument(1));
        return thisValue;
    }

    [JsHostMethod("get", Length = 1d)]
    public object? Get(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        return map.Get(args.GetArgument(0));
    }

    [JsHostMethod("has", Length = 1d)]
    public object Has(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        return map.Has(args.GetArgument(0));
    }

    [JsHostMethod("delete", Length = 1d)]
    public object Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        return map.Delete(args.GetArgument(0));
    }

    [JsHostMethod("clear", Length = 0d)]
    public object? Clear(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        map.Clear();
        return Symbol.Undefined;
    }

    [JsHostMethod("forEach", Length = 1d)]
    public object? ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireMap(thisValue);
        if (args.GetArgument(0) is not IJsCallable callback)
        {
            throw ThrowTypeError("Map.prototype.forEach callback must be callable", realm: Realm);
        }

        map.ForEach(callback, args.GetArgument(1));
        return Symbol.Undefined;
    }

    [JsHostMethod("entries", Length = 0d)]
    public object? Entries(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        return CreateMapIterator(map, MapIterationKind.Entries);
    }

    [JsHostMethod("keys", Length = 0d)]
    public object? Keys(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        return CreateMapIterator(map, MapIterationKind.Keys);
    }

    [JsHostMethod("values", Length = 0d)]
    public object? Values(object? thisValue, IReadOnlyList<object?> _)
    {
        var map = RequireMap(thisValue);
        return CreateMapIterator(map, MapIterationKind.Values);
    }

    [JsHostGetter("size")]
    public object Size(object? thisValue)
    {
        var map = RequireMap(thisValue);
        return (double)map.Size;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.MapPrototype ??= Prototype as JsObject;

        var iteratorKey = $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";
        if (Prototype.TryGetProperty("entries", out var entries))
        {
            Prototype.DefineProperty(iteratorKey,
                new PropertyDescriptor
                {
                    Value = entries,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
        }
    }

    private JsMap RequireMap(object? candidate)
    {
        if (candidate is JsMap map)
        {
            return map;
        }

        if (candidate is JsObject obj &&
            obj.GetOwnPropertyDescriptor("_internalMap")?.Value is JsMap inner)
        {
            return inner;
        }

        throw ThrowTypeError("Map method called on incompatible receiver", realm: Realm);
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
                object? value = kind switch
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

    private JsArray CreateEntryPair(object? first, object? second)
    {
        var pair = new JsArray(Realm);
        pair.SetElement(0, first);
        pair.SetElement(1, second);
        return pair;
    }
}

#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Map", PrototypeType = typeof(MapPrototype), Length = 0d, DisplayName = "Map")]
public sealed partial class MapConstructor(IJsObjectLike prototype, RealmState realm)
    : CollectionConstructorBase<JsMap>(prototype, realm, "Map")
{
    protected override JsMap CreateInstance()
    {
        return new JsMap();
    }

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.MapConstructor ??= constructor;
        Realm.MapPrototype ??= Prototype as JsObject;
    }

    protected override void PopulateInstance(JsMap instance, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        // ES spec: Map constructor step 7-8
        // 7. Let adder be ? Get(map, "set").
        // 8. If IsCallable(adder) is false, throw a TypeError exception.
        if (!instance.TryGetProperty("set", out var adderValue) ||
            !adderValue.TryGetObject<IJsCallable>(out var adder))
        {
            throw ThrowTypeError("Map.prototype.set is not callable", realm: Realm);
        }

        var instanceValue = JsValue.FromObjectUnsafe(instance);

        // ES spec: Map constructor step 9
        // Call adder with map as this and key, value as arguments
        // If abrupt, close iterator (handled by MapSetIterationHelper.Iterate)
        MapSetIterationHelper.Iterate(args[0], Realm, "Map constructor", entry =>
        {
            JsValue key;
            JsValue value;

            if (entry.TryGetObject<JsArray>(out var pair) && !pair.HasCustomIndexedProperties)
            {
                key = pair.GetElement(0);
                value = pair.GetElement(1);
            }
            else if (entry.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                accessor.TryGetProperty("0", out key);
                accessor.TryGetProperty("1", out value);
            }
            else
            {
                throw ThrowTypeError("Map constructor expects iterable entries", realm: Realm);
            }

            adder.Invoke([key, value], instanceValue);
        });
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    [JsConstructorMethod("groupBy", Length = 2d)]
    public static JsValue GroupBy(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw ThrowTypeError("Map.groupBy requires a realm", realm: null);
        }

        var items = args.GetArgument(0);
        var callbackFn = args.GetArgument(1);

        if (!callbackFn.TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("Map.groupBy callback must be a function", realm: realm);
        }

        var result = new JsMap();
        result.SetPrototype(realm.MapPrototype);

        var index = 0;
        MapSetIterationHelper.Iterate(items, realm, "Map.groupBy", element =>
        {
            var key = callback.Invoke([element, (double)index], JsValue.Undefined);
            index++;

            JsArray group;
            if (result.Has(key))
            {
                var existingGroup = result.Get(key);
                if (existingGroup.TryGetObject<JsArray>(out var existingArray))
                {
                    group = existingArray;
                }
                else
                {
                    group = new JsArray(realm);
                    result.Set(key, JsValue.FromJsArray(group));
                }
            }
            else
            {
                group = new JsArray(realm);
                result.Set(key, JsValue.FromJsArray(group));
            }

            group.SetElement((uint)group.Length, element);
        });

        return result.AsJsValue;
    }
}

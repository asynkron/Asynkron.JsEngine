#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Map", PrototypeType = typeof(MapPrototype), Length = 0d, DisplayName = "Map")]
public sealed partial class MapConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Map constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            return JsValue.FromObjectUnsafe(ConstructMap(args, _constructor ?? ConstructFallback,
                _constructor ?? ConstructFallback));
        }

        throw ThrowTypeError("Constructor Map requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.MapConstructor ??= constructor;
        Realm.MapPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("Constructor Map requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructMap(args, callable, target));
        });
    }

    private object ConstructMap(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = new JsMap();
        instance.SetPrototype(proto);
        PopulateMap(instance, args, Realm);
        return instance;
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    private static void PopulateMap(JsMap map, IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        MapSetIterationHelper.Iterate(args[0], realm, "Map constructor", entry =>
        {
            JsValue key;
            JsValue value;

            if (entry.TryGetObject<JsArray>(out var pair))
            {
                // Common fast path: iterables of [key, value] arrays.
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
                throw ThrowTypeError("Map constructor expects iterable entries", realm: realm);
            }

            map.Set(key, value);
        });
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
        
        // Validate callback
        if (!callbackFn.TryGetObject<IJsCallable>(out var callback) || callback is null)
        {
            throw ThrowTypeError("Map.groupBy callback must be a function", realm: realm);
        }
        
        // Create result Map
        var result = new JsMap();
        result.SetPrototype(realm.MapPrototype);

        // Group elements from any iterable.
        var index = 0;
        MapSetIterationHelper.Iterate(items, realm, "Map.groupBy", element =>
        {
            // Call callback with (element, index)
            var key = callback.Invoke([element, (double)index], JsValue.Undefined);
            index++;

            // Get or create array for this key.
            JsArray group;
            if (result.Has(key))
            {
                var existingGroup = result.Get(key);
                if (existingGroup.TryGetObject<JsArray>(out var existingArray) && existingArray is not null)
                {
                    group = existingArray;
                }
                else
                {
                    // Fallback: replace non-array values with a new grouping array.
                    group = new JsArray(realm);
                    result.Set(key, JsValue.FromJsArray(group));
                }
            }
            else
            {
                group = new JsArray(realm);
                result.Set(key, JsValue.FromJsArray(group));
            }

            // Add element to group.
            group.SetElement((uint)group.Length, element);
        });

        return result.AsJsValue;
    }
}

#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Map", ToStringTag = "Map", InstanceType = typeof(JsMap))]
[JsSymbolAlias("iterator", "entries")]
public sealed partial class MapPrototype
{
    [JsHostMethod("set", Length = 2d)]
    public JsValue Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        map.Set(args.GetArgument(0), args.GetArgument(1));
        return thisValue;
    }

    [JsHostMethod("get", Length = 1d)]
    public JsValue Get(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return map.Get(args.GetArgument(0));
    }

    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(map.Has(args.GetArgument(0)));
    }

    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(map.Delete(args.GetArgument(0)));
    }

    [JsHostMethod("clear", Length = 0d)]
    public JsValue Clear(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        map.Clear();
        return JsValue.Undefined;
    }

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

    [JsHostMethod("entries", Length = 0d)]
    public JsValue Entries(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        return CreateMapIterator(map, MapIterationKind.Entries);
    }

    [JsHostMethod("keys", Length = 0d)]
    public JsValue Keys(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        return CreateMapIterator(map, MapIterationKind.Keys);
    }

    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var map = RequireInstance(thisValue);
        return CreateMapIterator(map, MapIterationKind.Values);
    }

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

    private JsValue CreateMapIterator(JsMap map, MapIterationKind kind)
    {
        // Use the shared MapIteratorPrototype so @@iterator wiring stays consistent.
        var iteratorPrototype = Realm.MapIteratorPrototype ?? (Realm.MapIteratorPrototype = (JsObject)MapIteratorPrototype.CreatePrototype(Realm));
        var iterator = new JsMapIterator(map, kind, Realm, iteratorPrototype);
        return iterator.AsJsValue;
    }
}

#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Set", ToStringTag = "Set", InstanceType = typeof(JsSet))]
[JsSymbolAlias("iterator", "values")]
[JsMethodAlias("keys", "values")]
public sealed partial class SetPrototype
{
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireInstance(thisValue);
        set.Add(args.GetArgument(0));
        return thisValue;
    }

    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireInstance(thisValue);
        return new JsValue(set.Has(args.GetArgument(0)));
    }

    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireInstance(thisValue);
        return new JsValue(set.Delete(args.GetArgument(0)));
    }

    [JsHostMethod("clear", Length = 0d)]
    public JsValue Clear(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireInstance(thisValue);
        set.Clear();
        return JsValue.Undefined;
    }

    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireInstance(thisValue);
        if (!args.GetArgument(0).TryGetObject<IJsCallable>(out var callback))
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.forEach callback must be callable", realm: Realm);
        }

        set.ForEach(callback, args.GetArgument(1));
        return JsValue.Undefined;
    }

    [JsHostMethod("entries", Length = 0d)]
    public JsValue Entries(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireInstance(thisValue);
        return CreateSetIterator(set, SetIterationKind.Entries);
    }

    // keys is registered via code generation from [JsMethodAlias] attribute (ES spec: Set.prototype.keys === Set.prototype.values)

    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireInstance(thisValue);
        return CreateSetIterator(set, SetIterationKind.Values);
    }

    [JsHostGetter("size")]
    public JsValue Size(JsValue thisValue)
    {
        var set = RequireInstance(thisValue);
        return new JsValue((double)set.Size);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.SetPrototype ??= Prototype as JsObject;

        // [Symbol.iterator] is registered via code generation from [JsSymbolAlias] attribute
    }

    private JsValue CreateSetIterator(JsSet set, SetIterationKind kind)
    {
        // Use the shared SetIteratorPrototype so @@iterator wiring stays consistent.
        var iteratorPrototype = Realm.SetIteratorPrototype ??= (JsObject)SetIteratorPrototype.CreatePrototype(Realm);
        var iterator = new JsSetIterator(set, kind, Realm, iteratorPrototype);
        return iterator.AsJsValue;
    }

    [JsHostMethod("difference", Length = 1d)]
    public JsValue Difference(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        // Create result set
        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);

        var otherSet = GetOtherSetForMembership(otherValue, "Set.prototype.difference");

        // Add elements from this set that are not in the other set
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            var value = thisSet.GetValue(i);
            if (!otherSet.Has(value))
            {
                resultSet.Add(value);
            }
        }

        return resultSet.AsJsValue;
    }

    [JsHostMethod("intersection", Length = 1d)]
    public JsValue Intersection(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        // Create result set
        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);

        var otherSet = GetOtherSetForMembership(otherValue, "Set.prototype.intersection");

        // Add elements that exist in both sets
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            var value = thisSet.GetValue(i);
            if (otherSet.Has(value))
            {
                resultSet.Add(value);
            }
        }

        return resultSet.AsJsValue;
    }

    [JsHostMethod("isDisjointFrom", Length = 1d)]
    public JsValue IsDisjointFrom(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        var otherSet = GetOtherSetForMembership(otherValue, "Set.prototype.isDisjointFrom");

        // Check if there are any common elements
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            if (otherSet.Has(thisSet.GetValue(i)))
            {
                return false; // Found a common element, sets are not disjoint
            }
        }

        return true;
    }

    [JsHostMethod("isSubsetOf", Length = 1d)]
    public JsValue IsSubsetOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        var otherSet = GetOtherSetForMembership(otherValue, "Set.prototype.isSubsetOf");

        // Check if all elements of this set are in the other set
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            if (!otherSet.Has(thisSet.GetValue(i)))
            {
                return false; // Found an element not in other set
            }
        }

        return true;
    }

    [JsHostMethod("isSupersetOf", Length = 1d)]
    public JsValue IsSupersetOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        var otherSet = GetOtherSetForMembership(otherValue, "Set.prototype.isSupersetOf");

        // Check if all elements of the other set are in this set
        for (var i = 0; i < otherSet.ValueCount; i++)
        {
            if (!thisSet.Has(otherSet.GetValue(i)))
            {
                return false; // Found an element of other set not in this set
            }
        }

        return true;
    }

    [JsHostMethod("symmetricDifference", Length = 1d)]
    public JsValue SymmetricDifference(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        // Create result set
        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);

        var otherSet = GetOtherSetForMembership(otherValue, "Set.prototype.symmetricDifference");

        // Add elements from this set that are not in the other set
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            var value = thisSet.GetValue(i);
            if (!otherSet.Has(value))
            {
                resultSet.Add(value);
            }
        }

        // Add elements from the other set that are not in this set
        for (var i = 0; i < otherSet.ValueCount; i++)
        {
            var value = otherSet.GetValue(i);
            if (!thisSet.Has(value))
            {
                resultSet.Add(value);
            }
        }

        return resultSet.AsJsValue;
    }

    [JsHostMethod("union", Length = 1d)]
    public JsValue Union(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);

        // Create result set
        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);

        // Add all elements from this set
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            resultSet.Add(thisSet.GetValue(i));
        }

        // Add all elements from the other iterable (duplicates will be ignored).
        AddValuesFromIterable(resultSet, otherValue, "Set.prototype.union");

        return resultSet.AsJsValue;
    }

    private void AddValuesFromIterable(JsSet target, JsValue iterable, string methodName)
    {
        MapSetIterationHelper.Iterate(iterable, Realm, methodName, value => target.Add(value));
    }

    private JsSet GetOtherSetForMembership(JsValue otherValue, string methodName)
    {
        if (otherValue.TryGetObject<JsSet>(out var otherSet))
        {
            return otherSet;
        }

        // Build a temporary Set so we can use SameValueZero membership semantics.
        var tempSet = new JsSet();
        tempSet.SetPrototype(Realm.SetPrototype);
        AddValuesFromIterable(tempSet, otherValue, methodName);
        return tempSet;
    }
}

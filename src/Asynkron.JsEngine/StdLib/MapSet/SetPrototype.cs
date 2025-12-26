#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
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
        return new JsValue(CreateSetIterator(set, SetIterationKind.Entries));
    }

    // keys is registered via code generation from [JsMethodAlias] attribute (ES spec: Set.prototype.keys === Set.prototype.values)

    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireInstance(thisValue);
        return new JsValue(CreateSetIterator(set, SetIterationKind.Values));
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

    private JsObject CreateSetIterator(JsSet set, SetIterationKind kind)
    {
        var iterator = new JsObject { RealmState = Realm };
        var index = 0;

        iterator.SetHostedProperty("next", (_, _) =>
        {
            var result = new JsObject { RealmState = Realm };
            if (index < set.ValueCount)
            {
                var current = set.GetValue(index++);
                var value = kind switch
                {
                    SetIterationKind.Entries => JsValue.FromJsArray(CreateEntryPair(current, current)),
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

        var iteratorKey = SymbolKeys.Iterator;
        iterator.SetHostedProperty(iteratorKey, (_, _) => iterator);
        return iterator;
    }

    private JsArray CreateEntryPair(JsValue first, JsValue second)
    {
        var pair = new JsArray(Realm);
        pair.SetElement(0, first);
        pair.SetElement(1, second);
        return pair;
    }

    private enum SetIterationKind
    {
        Entries,
        Values
    }

    [JsHostMethod("difference", Length = 1d)]
    public JsValue Difference(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        
        // Create result set
        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);
        
        // Get other set - it could be a Set or any iterable
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.difference requires a Set argument", realm: Realm);
        }
        
        // Add elements from this set that are not in the other set
        foreach (var value in thisSet.Values())
        {
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
        
        // Get other set
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.intersection requires a Set argument", realm: Realm);
        }
        
        // Add elements that exist in both sets
        foreach (var value in thisSet.Values())
        {
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
        
        // Get other set
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.isDisjointFrom requires a Set argument", realm: Realm);
        }
        
        // Check if there are any common elements
        foreach (var value in thisSet.Values())
        {
            if (otherSet.Has(value))
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
        
        // Get other set
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.isSubsetOf requires a Set argument", realm: Realm);
        }
        
        // Check if all elements of this set are in the other set
        foreach (var value in thisSet.Values())
        {
            if (!otherSet.Has(value))
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
        
        // Get other set
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.isSupersetOf requires a Set argument", realm: Realm);
        }
        
        // Check if all elements of the other set are in this set
        foreach (var value in otherSet.Values())
        {
            if (!thisSet.Has(value))
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
        
        // Get other set
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.symmetricDifference requires a Set argument", realm: Realm);
        }
        
        // Add elements from this set that are not in the other set
        foreach (var value in thisSet.Values())
        {
            if (!otherSet.Has(value))
            {
                resultSet.Add(value);
            }
        }
        
        // Add elements from the other set that are not in this set
        foreach (var value in otherSet.Values())
        {
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
        
        // Get other set
        if (!otherValue.TryGetObject<JsSet>(out var otherSet) || otherSet is null)
        {
            throw StandardLibrary.ThrowTypeError("Set.prototype.union requires a Set argument", realm: Realm);
        }
        
        // Add all elements from this set
        foreach (var value in thisSet.Values())
        {
            resultSet.Add(value);
        }
        
        // Add all elements from the other set (duplicates will be ignored)
        foreach (var value in otherSet.Values())
        {
            resultSet.Add(value);
        }
        
        return resultSet.AsJsValue;
    }
}

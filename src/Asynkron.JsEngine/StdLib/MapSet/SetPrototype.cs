#region

using Asynkron.JsEngine.Runtime;
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
    }

    private JsValue CreateSetIterator(JsSet set, SetIterationKind kind)
    {
        var iteratorPrototype = Realm.SetIteratorPrototype ??= (JsObject)SetIteratorPrototype.CreatePrototype(Realm);
        var iterator = new JsSetIterator(set, kind, Realm, iteratorPrototype);
        return iterator.AsJsValue;
    }

    private SetRecord GetSetRecord(JsValue otherValue, string methodName)
    {
        if (!otherValue.TryGetObjectLike(out var otherObj))
        {
            throw StandardLibrary.ThrowTypeError(
                $"{methodName} requires a Set-like object as argument", realm: Realm);
        }

        otherObj.TryGetProperty("size", out var rawSize);
        var numSize = JsOps.ToNumber(rawSize);

        if (double.IsNaN(numSize))
        {
            throw StandardLibrary.ThrowTypeError(
                "GetSetRecord: size is not a valid number", realm: Realm);
        }

        var intSize = double.IsInfinity(numSize) ? numSize : Math.Truncate(numSize);
        if (intSize < 0)
        {
            intSize = 0;
        }

        if (!otherObj.TryGetProperty("has", out var hasValue) ||
            !hasValue.TryGetObject<IJsCallable>(out var hasCallable))
        {
            throw StandardLibrary.ThrowTypeError(
                $"{methodName} requires a Set-like object with a callable has method", realm: Realm);
        }

        if (!otherObj.TryGetProperty("keys", out var keysValue) ||
            !keysValue.TryGetObject<IJsCallable>(out var keysCallable))
        {
            throw StandardLibrary.ThrowTypeError(
                $"{methodName} requires a Set-like object with a callable keys method", realm: Realm);
        }

        return new SetRecord(otherObj, intSize, hasCallable, keysCallable);
    }

    private void IterateSetRecordKeys(SetRecord record, Action<JsValue> onValue)
    {
        var otherJsValue = JsValue.FromObjectUnsafe(record.Set);
        var iteratorResult = record.Keys.Invoke([], otherJsValue);
        if (!iteratorResult.TryGetObjectLike(out var iteratorObj))
        {
            throw StandardLibrary.ThrowTypeError("keys() must return an iterator object", realm: Realm);
        }

        if (!iteratorObj.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetObject<IJsCallable>(out var nextMethod))
        {
            throw StandardLibrary.ThrowTypeError("Iterator must have a callable next method", realm: Realm);
        }

        var iteratorJsValue = JsValue.FromObjectUnsafe(iteratorObj);
        while (true)
        {
            var stepResult = nextMethod.Invoke([], iteratorJsValue);
            if (!stepResult.TryGetObjectLike(out var stepObj))
            {
                throw StandardLibrary.ThrowTypeError("Iterator result must be an object", realm: Realm);
            }

            if (stepObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
            {
                break;
            }

            var value = stepObj.TryGetProperty("value", out var valueProp) ? valueProp : JsValue.Undefined;

            if (value.IsNumber && IsNegativeZero(value.NumberValue))
            {
                value = JsValue.Zero;
            }

            onValue(value);
        }
    }

    private void IterateSetRecordKeysWithEarlyExit(SetRecord record, Func<JsValue, bool> onValue)
    {
        var otherJsValue = JsValue.FromObjectUnsafe(record.Set);
        var iteratorResult = record.Keys.Invoke([], otherJsValue);
        if (!iteratorResult.TryGetObjectLike(out var iteratorObj))
        {
            throw StandardLibrary.ThrowTypeError("keys() must return an iterator object", realm: Realm);
        }

        if (!iteratorObj.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetObject<IJsCallable>(out var nextMethod))
        {
            throw StandardLibrary.ThrowTypeError("Iterator must have a callable next method", realm: Realm);
        }

        var iteratorJsValue = JsValue.FromObjectUnsafe(iteratorObj);
        while (true)
        {
            var stepResult = nextMethod.Invoke([], iteratorJsValue);
            if (!stepResult.TryGetObjectLike(out var stepObj))
            {
                throw StandardLibrary.ThrowTypeError("Iterator result must be an object", realm: Realm);
            }

            if (stepObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
            {
                break;
            }

            var value = stepObj.TryGetProperty("value", out var valueProp) ? valueProp : JsValue.Undefined;

            if (value.IsNumber && IsNegativeZero(value.NumberValue))
            {
                value = JsValue.Zero;
            }

            if (!onValue(value))
            {
                if (iteratorObj.TryGetProperty("return", out var returnProp) &&
                    returnProp.TryGetObject<IJsCallable>(out var returnMethod))
                {
                    returnMethod.Invoke([], iteratorJsValue);
                }

                break;
            }
        }
    }

    private bool CallHas(SetRecord record, JsValue value)
    {
        var otherJsValue = JsValue.FromObjectUnsafe(record.Set);
        var result = record.Has.Invoke(new SingleValueArgs(value), otherJsValue);
        return JsOps.ToBoolean(result);
    }

    [JsHostMethod("difference", Length = 1d)]
    public JsValue Difference(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.difference");

        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            resultSet.Add(thisSet.GetValue(i));
        }

        var thisSize = thisSet.Size;
        if (thisSize <= record.Size)
        {
            for (var i = 0; i < thisSet.ValueCount; i++)
            {
                var value = thisSet.GetValue(i);
                if (CallHas(record, value))
                {
                    resultSet.Delete(value);
                }
            }
        }
        else
        {
            IterateSetRecordKeys(record, value => resultSet.Delete(value));
        }

        return resultSet.AsJsValue;
    }

    [JsHostMethod("intersection", Length = 1d)]
    public JsValue Intersection(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.intersection");

        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);

        var thisSize = thisSet.Size;
        if (thisSize <= record.Size)
        {
            for (var i = 0; i < thisSet.ValueCount; i++)
            {
                var value = thisSet.GetValue(i);
                if (CallHas(record, value))
                {
                    resultSet.Add(value);
                }
            }
        }
        else
        {
            IterateSetRecordKeys(record, value =>
            {
                if (thisSet.Has(value))
                {
                    resultSet.Add(value);
                }
            });
        }

        return resultSet.AsJsValue;
    }

    [JsHostMethod("isDisjointFrom", Length = 1d)]
    public JsValue IsDisjointFrom(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.isDisjointFrom");

        var thisSize = thisSet.Size;
        if (thisSize <= record.Size)
        {
            for (var i = 0; i < thisSet.ValueCount; i++)
            {
                if (CallHas(record, thisSet.GetValue(i)))
                {
                    return false;
                }
            }
        }
        else
        {
            var result = true;
            IterateSetRecordKeysWithEarlyExit(record, value =>
            {
                if (thisSet.Has(value))
                {
                    result = false;
                    return false;
                }

                return true;
            });

            return result;
        }

        return true;
    }

    [JsHostMethod("isSubsetOf", Length = 1d)]
    public JsValue IsSubsetOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.isSubsetOf");

        var thisSize = thisSet.Size;
        if (thisSize > record.Size)
        {
            return false;
        }

        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            if (!CallHas(record, thisSet.GetValue(i)))
            {
                return false;
            }
        }

        return true;
    }

    [JsHostMethod("isSupersetOf", Length = 1d)]
    public JsValue IsSupersetOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.isSupersetOf");

        var thisSize = thisSet.Size;
        if (thisSize < record.Size)
        {
            return false;
        }

        var allFound = true;
        IterateSetRecordKeysWithEarlyExit(record, value =>
        {
            if (!thisSet.Has(value))
            {
                allFound = false;
                return false;
            }

            return true;
        });

        return allFound;
    }

    [JsHostMethod("symmetricDifference", Length = 1d)]
    public JsValue SymmetricDifference(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.symmetricDifference");

        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            resultSet.Add(thisSet.GetValue(i));
        }

        IterateSetRecordKeys(record, value =>
        {
            if (thisSet.Has(value))
            {
                resultSet.Delete(value);
            }
            else
            {
                resultSet.Add(value);
            }
        });

        return resultSet.AsJsValue;
    }

    [JsHostMethod("union", Length = 1d)]
    public JsValue Union(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var thisSet = RequireInstance(thisValue);
        var otherValue = args.GetArgument(0);
        var record = GetSetRecord(otherValue, "Set.prototype.union");

        var resultSet = new JsSet();
        resultSet.SetPrototype(Realm.SetPrototype);
        for (var i = 0; i < thisSet.ValueCount; i++)
        {
            resultSet.Add(thisSet.GetValue(i));
        }

        IterateSetRecordKeys(record, value => resultSet.Add(value));

        return resultSet.AsJsValue;
    }

    private static bool IsNegativeZero(double value)
    {
        return value == 0.0 && double.IsNegativeInfinity(1.0 / value);
    }

    private readonly record struct SetRecord(IJsObjectLike Set, double Size, IJsCallable Has, IJsCallable Keys);
}

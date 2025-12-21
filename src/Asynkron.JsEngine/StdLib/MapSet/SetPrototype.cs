using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Set", ToStringTag = "Set")]
public sealed partial class SetPrototype
{
    private enum SetIterationKind
    {
        Entries,
        Keys,
        Values
    }

    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireSet(thisValue);
        set.Add(args.GetArgument(0));
        return thisValue;
    }

    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireSet(thisValue);
        return new JsValue(set.Has(args.GetArgument(0)));
    }

    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireSet(thisValue);
        return new JsValue(set.Delete(args.GetArgument(0)));
    }

    [JsHostMethod("clear", Length = 0d)]
    public JsValue Clear(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireSet(thisValue);
        set.Clear();
        return JsValue.Undefined;
    }

    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireSet(thisValue);
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
        var set = RequireSet(thisValue);
        return new JsValue(CreateSetIterator(set, SetIterationKind.Entries));
    }

    [JsHostMethod("keys", Length = 0d)]
    public JsValue Keys(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireSet(thisValue);
        return new JsValue(CreateSetIterator(set, SetIterationKind.Keys));
    }

    [JsHostMethod("values", Length = 0d)]
    public JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var set = RequireSet(thisValue);
        return new JsValue(CreateSetIterator(set, SetIterationKind.Values));
    }

    [JsHostGetter("size")]
    public JsValue Size(JsValue thisValue)
    {
        var set = RequireSet(thisValue);
        return new JsValue((double)set.Size);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.SetPrototype ??= Prototype as JsObject;

        var iteratorKey = SymbolKeys.Iterator;
        if (Prototype.TryGetProperty("values", out var values))
        {
            Prototype.DefineProperty(iteratorKey,
                new PropertyDescriptor
                {
                    Value = values,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
        }
    }

    private JsSet RequireSet(JsValue receiver)
    {
        var candidate = receiver.ToObject();

        if (candidate is JsSet set)
        {
            return set;
        }

        if (candidate is JsObject obj &&
            obj.GetOwnPropertyDescriptor("_internalSet")?.Value is JsSet inner)
        {
            return inner;
        }

        throw StandardLibrary.ThrowTypeError("Set method called on incompatible receiver", realm: Realm);
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
                    SetIterationKind.Entries => JsValue.FromObjectUnsafe(CreateEntryPair(current, current)),
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
}

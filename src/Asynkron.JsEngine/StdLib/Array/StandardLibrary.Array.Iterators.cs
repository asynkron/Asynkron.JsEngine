#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Specifies the kind of array iterator to create.
/// Using an enum avoids closure allocations compared to passing Func delegates.
/// </summary>
internal enum ArrayIteratorKind
{
    /// <summary>Iterator yields [index, value] pairs.</summary>
    Entries,
    /// <summary>Iterator yields index values only.</summary>
    Keys,
    /// <summary>Iterator yields element values only.</summary>
    Values
}

public static partial class StandardLibrary
{
    /// <summary>
    /// Creates an array iterator without closure allocations.
    /// </summary>
    internal static object CreateArrayIterator(JsValue thisValue, string methodName, RealmState? realm,
        ArrayIteratorKind kind)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        return CreateArrayIteratorObject(accessor, kind, realm);
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, ArrayIteratorKind kind,
        RealmState? realm)
    {
        JsObject? iteratorPrototype = null;
        if (realm is not null)
        {
            iteratorPrototype = realm.ArrayIteratorPrototype
                                ?? (realm.ArrayIteratorPrototype = (JsObject)ArrayIteratorPrototype.CreatePrototype(realm));
        }

        var iterator = new JsArrayIterator(accessor, kind, realm, iteratorPrototype);
        return iterator;
    }
}

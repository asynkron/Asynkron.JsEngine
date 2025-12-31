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

    /// <summary>
    /// Legacy overload for backwards compatibility - still allocates closures.
    /// Prefer the ArrayIteratorKind overload for new code.
    /// </summary>
    internal static object CreateArrayIterator(JsValue thisValue, string methodName, RealmState? realm,
        Func<IJsPropertyAccessor, JsValue, Func<uint, JsValue>> projectorFactory)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        var projector = projectorFactory(accessor, thisValue);
        return CreateArrayIteratorObject(accessor, projector, realm);
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, ArrayIteratorKind kind,
        RealmState? realm)
    {
        var iteratorPrototype = GetArrayIteratorPrototype(realm);
        return new JsArrayIterator(accessor, kind, realm, iteratorPrototype);
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, Func<uint, JsValue> projector,
        RealmState? realm)
    {
        var iteratorPrototype = GetArrayIteratorPrototype(realm);
        return new JsArrayIterator(accessor, projector, realm, iteratorPrototype);
    }

    private static JsObject? GetArrayIteratorPrototype(RealmState? realm)
    {
        if (realm is null)
        {
            return null;
        }

        if (realm.ArrayIteratorPrototype is null)
        {
            realm.ArrayIteratorPrototype = (JsObject)ArrayIteratorPrototype.CreatePrototype(realm);
        }

        return realm.ArrayIteratorPrototype;
    }
}

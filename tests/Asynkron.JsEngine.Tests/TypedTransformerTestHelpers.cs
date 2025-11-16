using System.Collections.Generic;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Tests;

internal static class TypedTransformerTestHelpers
{
    public static Cons CloneWithoutSourceReferences(Cons cons)
    {
        ArgumentNullException.ThrowIfNull(cons);
        return CloneInternal(cons);
    }

    private static Cons CloneInternal(Cons cons)
    {
        if (cons.IsEmpty)
        {
            return Cons.Empty;
        }

        var items = new List<object?>();
        foreach (var item in cons)
        {
            items.Add(item is Cons nested ? CloneInternal(nested) : item);
        }

        return Cons.FromEnumerable(items);
    }
}

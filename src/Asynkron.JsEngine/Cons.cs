using System.Collections;
using System.Text;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a cons cell backed S-expression list.
/// </summary>
public sealed class Cons : IEnumerable<object?>
{
    private static readonly Cons EmptyInstance = new();
    private readonly object? _head;
    private readonly Cons _tail;

    private Cons()
    {
        IsEmpty = true;
        _tail = this;
    }

    private Cons(object? head, Cons? tail)
    {
        _head = head;
        _tail = tail ?? EmptyInstance;
        IsEmpty = false;
    }

    /// <summary>
    /// Gets the shared empty list instance.
    /// </summary>
    public static Cons Empty => EmptyInstance;

    /// <summary>
    /// Indicates whether the cons cell represents the empty list.
    /// </summary>
    public bool IsEmpty { get; }

    /// <summary>
    /// The head (car) of the cell.
    /// </summary>
    public object? Head
    {
        get
        {
            if (IsEmpty)
            {
                throw new InvalidOperationException("The empty list does not have a head.");
            }

            return _head;
        }
    }

    /// <summary>
    /// The tail (cdr) of the cell.
    /// </summary>
    public Cons Rest
    {
        get
        {
            if (IsEmpty)
            {
                throw new InvalidOperationException("The empty list does not have a rest.");
            }

            return _tail;
        }
    }

    /// <summary>
    /// Creates a cons cell from a head and optional rest.
    /// </summary>
    public static Cons Cell(object? head, Cons? rest = null)
        => new(head, rest);

    /// <summary>
    /// Builds a list from the supplied items.
    /// </summary>
    public static Cons List(params object?[] items)
        => FromEnumerable(items);

    /// <summary>
    /// Builds a list from an enumerable sequence.
    /// </summary>
    public static Cons FromEnumerable(IEnumerable<object?> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (items is object?[] array)
        {
            return FromArray(array);
        }

        var stack = new Stack<object?>();
        foreach (var item in items)
        {
            stack.Push(item);
        }

        return FromStack(stack);
    }

    private static Cons FromArray(object?[] array)
    {
        var current = EmptyInstance;
        for (var i = array.Length - 1; i >= 0; i--)
        {
            current = new Cons(array[i], current);
        }

        return current;
    }

    private static Cons FromStack(Stack<object?> stack)
    {
        var current = EmptyInstance;
        while (stack.Count > 0)
        {
            current = new Cons(stack.Pop(), current);
        }

        return current;
    }

    /// <summary>
    /// Returns the element at the specified index.
    /// </summary>
    public object? ElementAt(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var current = this;
        var position = 0;
        while (!current.IsEmpty)
        {
            if (position == index)
            {
                return current._head;
            }

            current = current._tail;
            position++;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Enumerates the list.
    /// </summary>
    public IEnumerator<object?> GetEnumerator()
    {
        var current = this;
        while (!current.IsEmpty)
        {
            yield return current._head;
            current = current._tail;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "()";
        }

        var builder = new StringBuilder("(");
        var first = true;
        foreach (var item in this)
        {
            if (!first)
            {
                builder.Append(' ');
            }

            builder.Append(FormatAtom(item));
            first = false;
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static string FormatAtom(object? atom) => atom switch
    {
        null => "null",
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        bool b => b ? "true" : "false",
        Cons cons => cons.ToString(),
        _ => atom.ToString() ?? string.Empty
    };
}

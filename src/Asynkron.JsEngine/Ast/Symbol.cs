using System.Collections.Concurrent;

namespace Asynkron.JsEngine.Ast;


public sealed class Symbol : IEquatable<Symbol>
{
    private static readonly ConcurrentDictionary<string, Symbol> Cache = new(StringComparer.Ordinal);
    private static int NextId;
    private readonly int _id;

    private Symbol(string name)
    {
        Name = name;
        _id = Interlocked.Increment(ref NextId);
    }

    /// <summary>
    ///     Gets the textual representation of the symbol.
    /// </summary>
    public string Name { get; }

    public bool Equals(Symbol? other)
    {
        return other is not null && ReferenceEquals(this, other);
    }

    /// <summary>
    ///     Returns an interned symbol for the given name.
    /// </summary>
    public static Symbol Intern(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Symbol names must contain at least one non-whitespace character.",
                nameof(name));
        }

        return Cache.GetOrAdd(name, n => new Symbol(n));
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Symbol);
    }

    public override int GetHashCode()
    {
        return _id;
    }

    public override string ToString()
    {
        return Name;
    }

    public static readonly Symbol Undefined = Intern("undefined");
    public static readonly Symbol This = Intern("this");
    public static readonly Symbol Super = Intern("super");
    public static readonly Symbol NewTarget = Intern("new.target");
    public static readonly Symbol ThisInitialized = Intern("[[thisInitialized]]");
    public static readonly Symbol Arguments = Intern("arguments");
}

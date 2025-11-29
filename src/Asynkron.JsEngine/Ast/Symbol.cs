using System.Collections.Concurrent;

namespace Asynkron.JsEngine.Ast;

public sealed class Symbol : IEquatable<Symbol>
{
    private static readonly ConcurrentDictionary<string, Symbol> Cache = new(StringComparer.Ordinal);
    private static int NextId;

    public static readonly Symbol Undefined = Intern("undefined");
    public static readonly Symbol This = Intern("this");
    public static readonly Symbol Super = Intern("super");
    public static readonly Symbol NewTarget = Intern("new.target");
    public static readonly Symbol ThisInitialized = Intern("[[thisInitialized]]");
    public static readonly Symbol Arguments = Intern("arguments");
    public static readonly Symbol YieldTrackerSymbol = Intern("__yieldTracker__");
    public static readonly Symbol YieldResumeContextSymbol = Intern("__yieldResume__");
    public static readonly Symbol GeneratorPendingCompletionSymbol = Intern("__generatorPending__");
    public static readonly Symbol GeneratorInstanceSymbol = Intern("__generatorInstance__");
    public static readonly Symbol PromiseIdentifier = Intern("Promise");
    public static readonly Symbol ResolveIdentifier = Intern("__resolve");
    public static readonly Symbol RejectIdentifier = Intern("__reject");
    public static readonly Symbol AwaitHelperIdentifier = Intern("__awaitHelper");
    public static readonly Symbol AwaitValueIdentifier = Intern("__value");
    public static readonly Symbol CatchIdentifier = Intern("__error");
    public static readonly Symbol SyntaxErrorIdentifier = Intern("SyntaxError");
    public static readonly Symbol TypeErrorIdentifier = Intern("TypeError");
    public static readonly Symbol ReferenceErrorIdentifier = Intern("ReferenceError");
    public static readonly Symbol DebugIdentifier = Intern("__debug");
    public static readonly Symbol GetAsyncIteratorIdentifier = Intern("__getAsyncIterator");
    public static readonly Symbol IteratorNextIdentifier = Intern("__iteratorNext");
    public static readonly Symbol AwaitErrorIdentifier = Intern("__awaitError");
    public static readonly Symbol LoopValueIdentifier = Intern("__loopValue");
    public static readonly Symbol ArgsIdentifier = Intern("args");
    public static readonly Symbol SymbolConstructorIdentifier = Intern("Symbol");
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
}

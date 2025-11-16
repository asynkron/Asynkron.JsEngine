using System.Diagnostics.CodeAnalysis;

namespace Asynkron.JsEngine.Lisp;

/// <summary>
/// Extension methods for Cons that provide checks for constant expressions.
/// These are used by the constant expression transformer to identify values that can be folded.
/// </summary>
public static class ConsExtensions
{
    /// <summary>
    /// Checks if the given object is a constant number (double).
    /// </summary>
    public static bool IsConstantNumber(this object? obj)
    {
        return obj is double;
    }

    /// <summary>
    /// Checks if the given object is a constant string.
    /// </summary>
    public static bool IsConstantString(this object? obj)
    {
        return obj is string;
    }

    /// <summary>
    /// Checks if the given object is a constant boolean.
    /// </summary>
    public static bool IsConstantBoolean(this object? obj)
    {
        return obj is bool;
    }

    /// <summary>
    /// Checks if the given object is a constant null value.
    /// </summary>
    public static bool IsConstantNull(this object? obj)
    {
        return obj == null;
    }

    /// <summary>
    /// Checks if the given object is any constant literal value
    /// (number, string, boolean, or null).
    /// </summary>
    public static bool IsConstant(this object? obj)
    {
        return obj.IsConstantNumber() ||
               obj.IsConstantString() ||
               obj.IsConstantBoolean() ||
               obj.IsConstantNull();
    }

    /// <summary>
    /// Attempts to match the cons against a specific tag symbol and returns the remaining arguments.
    /// </summary>
    public static bool TryMatch(this Cons cons, Symbol expectedTag, out Cons args)
    {
        if (!cons.IsEmpty && cons.Head is Symbol tag && ReferenceEquals(tag, expectedTag))
        {
            args = cons.Rest;
            return true;
        }

        args = Cons.Empty;
        return false;
    }

    private ref struct ConsWalker
    {
        private Cons _current;

        public ConsWalker(Cons start)
        {
            _current = start;
        }

        public bool TryTake(out object? value)
        {
            if (_current.IsEmpty)
            {
                value = null;
                return false;
            }

            value = _current.Head;
            _current = _current.Rest;
            return true;
        }

        public Cons Remaining => _current;
    }

    /// <summary>
    /// Extracts a single argument and ensures there are no remaining items.
    /// </summary>
    public static bool TryGetArguments(this Cons args, out object? first)
    {
        first = null;
        var walker = new ConsWalker(args);

        if (!walker.TryTake(out first) || !walker.Remaining.IsEmpty)
        {
            first = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the first argument and returns the remaining list.
    /// </summary>
    public static bool TryGetArgumentsWithRemainder(this Cons args, out object? first, out Cons remainder)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first))
        {
            remainder = Cons.Empty;
            return false;
        }

        remainder = walker.Remaining;
        return true;
    }

    /// <summary>
    /// Extracts exactly two arguments.
    /// </summary>
    public static bool TryGetArguments(this Cons args, out object? first, out object? second)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first) || !walker.TryTake(out second) || !walker.Remaining.IsEmpty)
        {
            first = second = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts two arguments and returns any remaining items.
    /// </summary>
    public static bool TryGetArgumentsWithRemainder(this Cons args, out object? first, out object? second, out Cons remainder)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first) || !walker.TryTake(out second))
        {
            remainder = Cons.Empty;
            first = second = null;
            return false;
        }

        remainder = walker.Remaining;
        return true;
    }

    /// <summary>
    /// Extracts exactly three arguments.
    /// </summary>
    public static bool TryGetArguments(this Cons args, out object? first, out object? second, out object? third)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first) || !walker.TryTake(out second) || !walker.TryTake(out third) ||
            !walker.Remaining.IsEmpty)
        {
            first = second = third = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts three arguments and returns any remaining items.
    /// </summary>
    public static bool TryGetArgumentsWithRemainder(this Cons args, out object? first, out object? second, out object? third,
        out Cons remainder)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first) || !walker.TryTake(out second) || !walker.TryTake(out third))
        {
            remainder = Cons.Empty;
            first = second = third = null;
            return false;
        }

        remainder = walker.Remaining;
        return true;
    }

    /// <summary>
    /// Extracts exactly four arguments.
    /// </summary>
    public static bool TryGetArguments(this Cons args, out object? first, out object? second, out object? third,
        out object? fourth)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first) || !walker.TryTake(out second) || !walker.TryTake(out third) ||
            !walker.TryTake(out fourth) || !walker.Remaining.IsEmpty)
        {
            first = second = third = fourth = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts four arguments and returns any remaining items.
    /// </summary>
    public static bool TryGetArgumentsWithRemainder(this Cons args, out object? first, out object? second, out object? third,
        out object? fourth, out Cons remainder)
    {
        var walker = new ConsWalker(args);
        if (!walker.TryTake(out first) || !walker.TryTake(out second) || !walker.TryTake(out third) ||
            !walker.TryTake(out fourth))
        {
            remainder = Cons.Empty;
            first = second = third = fourth = null;
            return false;
        }

        remainder = walker.Remaining;
        return true;
    }

    /// <summary>
    /// Attempts to interpret the cons as an if statement.
    /// </summary>
    public static bool TryAsIfStatement(this Cons cons, out object? condition, out object? thenBranch, out object? elseBranch)
    {
        condition = thenBranch = elseBranch = null;

        if (!cons.TryMatch(JsSymbols.If, out var args))
        {
            return false;
        }

        if (!args.TryGetArgumentsWithRemainder(out condition, out thenBranch, out var remainder))
        {
            condition = thenBranch = elseBranch = null;
            return false;
        }

        if (!remainder.IsEmpty)
        {
            if (!remainder.TryGetArguments(out elseBranch))
            {
                condition = thenBranch = elseBranch = null;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to interpret the cons as a while statement (condition, body).
    /// </summary>
    public static bool TryAsWhileStatement(this Cons cons, out object? condition, out object? body)
    {
        condition = body = null;
        return cons.TryMatch(JsSymbols.While, out var args) && args.TryGetArguments(out condition, out body);
    }

    /// <summary>
    /// Attempts to interpret the cons as a do/while statement (condition, body).
    /// </summary>
    public static bool TryAsDoWhileStatement(this Cons cons, out object? condition, out object? body)
    {
        condition = body = null;
        return cons.TryMatch(JsSymbols.DoWhile, out var args) && args.TryGetArguments(out condition, out body);
    }

    /// <summary>
    /// Attempts to interpret the cons as a traditional for loop (initializer, condition, increment, body).
    /// </summary>
    public static bool TryAsForStatement(this Cons cons, out object? initializer, out object? condition, out object? increment,
        out object? body)
    {
        initializer = condition = increment = body = null;
        return cons.TryMatch(JsSymbols.For, out var args) &&
               args.TryGetArguments(out initializer, out condition, out increment, out body);
    }

    /// <summary>
    /// Attempts to interpret the cons as an iterable loop (binding, iterable, body).
    /// </summary>
    private static bool TryAsIterableLoop(this Cons cons, Symbol expectedTag, out object? binding, out object? iterable,
        out object? body)
    {
        binding = iterable = body = null;
        return cons.TryMatch(expectedTag, out var args) && args.TryGetArguments(out binding, out iterable, out body);
    }

    public static bool TryAsForInStatement(this Cons cons, out object? binding, out object? iterable, out object? body)
    {
        return cons.TryAsIterableLoop(JsSymbols.ForIn, out binding, out iterable, out body);
    }

    public static bool TryAsForOfStatement(this Cons cons, out object? binding, out object? iterable, out object? body)
    {
        return cons.TryAsIterableLoop(JsSymbols.ForOf, out binding, out iterable, out body);
    }

    public static bool TryAsForAwaitOfStatement(this Cons cons, out object? binding, out object? iterable, out object? body)
    {
        return cons.TryAsIterableLoop(JsSymbols.ForAwaitOf, out binding, out iterable, out body);
    }

    /// <summary>
    /// Attempts to interpret the cons as a labeled statement (label symbol, inner statement).
    /// </summary>
    public static bool TryAsLabelStatement(this Cons cons, [NotNullWhen(true)] out Symbol? labelName, out object? statement)
    {
        labelName = null;
        statement = null;

        if (!cons.TryMatch(JsSymbols.Label, out var args))
        {
            return false;
        }

        if (!args.TryGetArguments(out var labelCandidate, out statement))
        {
            labelName = null;
            statement = null;
            return false;
        }

        labelName = labelCandidate as Symbol;
        return labelName is not null;
    }
}

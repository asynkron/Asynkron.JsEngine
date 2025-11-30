using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine;

internal static class ArgumentListExtensions
{
    public static object? GetArgument(this IReadOnlyList<object?> args, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index < args.Count ? args[index] : Symbol.Undefined;
    }
}

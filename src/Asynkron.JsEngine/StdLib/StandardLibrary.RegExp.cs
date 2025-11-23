using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateRegExpConstructor(RealmState realm)
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return CreateRegExpLiteral("(?:)", "", realm);
            }

            if (args is [JsObject { } existingObj] &&
                existingObj.TryGetProperty("__regex__", out var internalRegex) &&
                internalRegex is JsRegExp existing)
            {
                return CreateRegExpLiteral(existing.Pattern, existing.Flags, realm);
            }

            var pattern = args[0]?.ToString() ?? "";
            var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            return CreateRegExpLiteral(pattern, flags, realm);
        });
    }

    internal static JsObject CreateRegExpLiteral(string pattern, string flags, RealmState? realm = null)
    {
        var regex = new JsRegExp(pattern, flags, realm);
        regex.JsObject["__regex__"] = regex;
        AddRegExpMethods(regex, realm);
        return regex.JsObject;
    }

    /// <summary>
    ///     Adds RegExp instance methods to a JsRegExp object.
    /// </summary>
    private static void AddRegExpMethods(JsRegExp regex, RealmState? realm)
    {
        // test(string) - returns boolean
        regex.JsObject.SetHostedProperty("test", RegExpTest);

        // exec(string) - returns array with match details or null
        regex.JsObject.SetHostedProperty("exec", RegExpExec);
    }

    private static object? RegExpTest(object? thisValue, IReadOnlyList<object?> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return false;
        }

        if (args.Count == 0)
        {
            return false;
        }

        var input = args[0]?.ToString() ?? string.Empty;
        return resolved.Test(input);
    }

    private static object? RegExpExec(object? thisValue, IReadOnlyList<object?> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return null;
        }

        if (args.Count == 0)
        {
            return null;
        }

        var input = args[0]?.ToString() ?? string.Empty;
        return resolved.Exec(input);
    }

    private static JsRegExp? ResolveRegExpInstance(object? thisValue)
    {
        if (thisValue is JsRegExp direct)
        {
            return direct;
        }

        if (thisValue is JsObject obj &&
            obj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex is JsRegExp stored)
        {
            return stored;
        }

        return null;
    }
}

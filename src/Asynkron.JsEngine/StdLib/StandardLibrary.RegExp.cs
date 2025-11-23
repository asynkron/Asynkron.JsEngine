using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using System.Text.RegularExpressions;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateRegExpConstructor(RealmState realm)
    {
        var prototype = new JsObject();
        realm.RegExpPrototype = prototype;

        var constructor = new HostFunction(args =>
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
        })
        {
            IsConstructor = true,
            RealmState = realm
        };

        constructor.SetProperty("prototype", prototype);
        prototype.SetProperty("constructor", constructor);
        return constructor;
    }

    internal static JsObject CreateRegExpLiteral(string pattern, string flags, RealmState? realm = null)
    {
        try
        {
            ValidateGroupNames(pattern);
            var regex = new JsRegExp(pattern, flags, realm);
            regex.JsObject["__regex__"] = regex;
            if (realm?.RegExpPrototype is not null)
            {
                regex.JsObject.SetPrototype(realm.RegExpPrototype);
            }
            AddRegExpMethods(regex, realm);
            return regex.JsObject;
        }
        catch (ParseException ex)
        {
            var error = CreateSyntaxError(ex.Message, realm);
            throw new ThrowSignal(error);
        }
        catch (ArgumentException ex)
        {
            var error = CreateSyntaxError(ex.Message, realm);
            throw new ThrowSignal(error);
        }
    }

    private static void ValidateGroupNames(string pattern)
    {
        var index = 0;
        while (true)
        {
            var start = pattern.IndexOf("(?<", index, StringComparison.Ordinal);
            if (start == -1)
            {
                break;
            }

            var end = pattern.IndexOf('>', start + 3);
            if (end == -1)
            {
                break;
            }

            var name = pattern.Substring(start + 3, end - (start + 3));
            JsRegExp.NormalizeGroupNameToken(name);
            index = end + 1;
        }
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

    private static object CreateSyntaxError(string message, RealmState? realm)
    {
        if (realm?.SyntaxErrorConstructor is { } ctor)
        {
            try
            {
                return ctor.Invoke([message], Symbols.Undefined);
            }
            catch (ThrowSignal signal)
            {
                return signal.ThrownValue ?? message;
            }
        }

        return message;
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

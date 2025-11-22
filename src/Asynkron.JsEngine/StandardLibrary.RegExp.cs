using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static IJsCallable CreateRegExpConstructor()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return CreateRegExpLiteral("(?:)", "");
            }

            if (args.Count == 1 && args[0] is JsObject { } existingObj &&
                existingObj.TryGetProperty("__regex__", out var internalRegex) &&
                internalRegex is JsRegExp existing)
            {
                return CreateRegExpLiteral(existing.Pattern, existing.Flags);
            }

            var pattern = args[0]?.ToString() ?? "";
            var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            return CreateRegExpLiteral(pattern, flags);
        });
    }

    internal static JsObject CreateRegExpLiteral(string pattern, string flags)
    {
        var regex = new JsRegExp(pattern, flags);
        regex.JsObject["__regex__"] = regex;
        AddRegExpMethods(regex);
        return regex.JsObject;
    }

    /// <summary>
    /// Adds RegExp instance methods to a JsRegExp object.
    /// </summary>
    private static void AddRegExpMethods(JsRegExp regex)
    {
        // test(string) - returns boolean
        regex.SetProperty("test", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            var input = args[0]?.ToString() ?? "";
            return regex.Test(input);
        }));

        // exec(string) - returns array with match details or null
        regex.SetProperty("exec", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var input = args[0]?.ToString() ?? "";
            return regex.Exec(input);
        }));
    }


}

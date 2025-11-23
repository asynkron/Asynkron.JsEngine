using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateEscapeFunction()
    {
        var escapeFn = new HostFunction(args =>
        {
            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            if (value is Symbol or TypedAstSymbol)
            {
                throw ThrowTypeError("Cannot convert a Symbol value to a string");
            }

            var input = JsOps.ToJsString(value);
            var builder = new StringBuilder(input.Length * 2);
            foreach (var ch in input)
            {
                if (IsUnescaped(ch))
                {
                    builder.Append(ch);
                    continue;
                }

                var code = (int)ch;
                if (code <= 0xFF)
                {
                    builder.Append('%');
                    builder.Append(code.ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append("%u");
                    builder.Append(code.ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }) { IsConstructor = false };

        escapeFn.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        escapeFn.DefineProperty("name",
            new PropertyDescriptor { Value = "escape", Writable = false, Enumerable = false, Configurable = true });
        // Built-in functions should not expose an own prototype property.
        escapeFn.Properties.DeleteOwnProperty("prototype");

        return escapeFn;
    }

    public static HostFunction CreateUnescapeFunction()
    {
        var unescapeFn = new HostFunction(args =>
        {
            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            if (value is Symbol or TypedAstSymbol)
            {
                throw ThrowTypeError("Cannot convert a Symbol value to a string");
            }

            var input = JsOps.ToJsString(value);
            var builder = new StringBuilder(input.Length);

            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (ch != '%' || i + 1 >= input.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                var next = input[i + 1];
                // %uXXXX form
                if ((next == 'u' || next == 'U') && i + 5 < input.Length &&
                    IsHex(input[i + 2]) && IsHex(input[i + 3]) && IsHex(input[i + 4]) && IsHex(input[i + 5]))
                {
                    var code = Convert.ToInt32(input.Substring(i + 2, 4), 16);
                    builder.Append((char)code);
                    i += 5;
                    continue;
                }

                // %XX form
                if (IsHex(next) && i + 2 < input.Length && IsHex(input[i + 2]))
                {
                    var code = Convert.ToInt32(input.Substring(i + 1, 2), 16);
                    builder.Append((char)code);
                    i += 2;
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }) { IsConstructor = false };

        unescapeFn.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        unescapeFn.DefineProperty("name",
            new PropertyDescriptor { Value = "unescape", Writable = false, Enumerable = false, Configurable = true });
        unescapeFn.Properties.DeleteOwnProperty("prototype");

        return unescapeFn;
    }

    private static bool IsUnescaped(char ch)
    {
        return ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '@' or '*' or '_' or '+' or '-' or '.' or '/';
    }

    private static bool IsHex(char ch)
    {
        return ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}

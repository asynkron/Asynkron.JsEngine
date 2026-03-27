#region

using System.Globalization;
using System.Text;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.TypedArray;

/// <summary>
/// Implements Uint8Array base64 and hex methods (ES2025 uint8array-base64 proposal).
/// Static: Uint8Array.fromBase64(string, options?), Uint8Array.fromHex(string)
/// Instance: toBase64(options?), toHex(), setFromBase64(string, options?), setFromHex(string)
/// </summary>
internal static class Uint8ArrayBase64
{
    private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    private const string Base64UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Register base64/hex methods on the Uint8Array constructor and prototype.
    /// </summary>
    public static void Register(HostFunction constructor, RealmState realm)
    {
        // Static methods on constructor
        AddMethod(constructor, "fromBase64", (_, args) => FromBase64(args, realm), 1, realm);
        AddMethod(constructor, "fromHex", (_, args) => FromHex(args, realm), 1, realm);

        // Instance methods on prototype
        if (constructor.TryGetProperty("prototype", out var protoVal) &&
            protoVal.TryGetObject<JsObject>(out var prototype))
        {
            AddMethod(prototype, "toBase64", (thisVal, args) => ToBase64(thisVal, args, realm), 0, realm);
            AddMethod(prototype, "toHex", (thisVal, _) => ToHex(thisVal, realm), 0, realm);
            AddMethod(prototype, "setFromBase64", (thisVal, args) => SetFromBase64(thisVal, args, realm), 1, realm);
            AddMethod(prototype, "setFromHex", (thisVal, args) => SetFromHex(thisVal, args, realm), 1, realm);
        }
    }

    private static void AddMethod(IJsPropertyAccessor target, string name, JsHostHandler handler,
        int length, RealmState realm)
    {
        var fn = new HostFunction(handler, realm, false);
        fn.DefineProperty("name", new PropertyDescriptor
        {
            Value = (JsValue)name, Writable = false, Enumerable = false, Configurable = true
        });
        fn.DefineProperty("length", new PropertyDescriptor
        {
            Value = JsValue.FromDouble(length), Writable = false, Enumerable = false, Configurable = true
        });
        target.SetHostedProperty(name, fn, realm);
    }

    private static JsValue FromBase64(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var input = args.GetArgument(0);

        // Step 1: If input is not a string, throw TypeError
        if (!input.IsString)
        {
            throw ThrowTypeError("Uint8Array.fromBase64 requires a string argument", realm: realm);
        }

        var str = input.AsString();

        // Step 2: Parse options
        var options = args.GetArgument(1);
        var (alphabet, lastChunkHandling) = ParseBase64Options(options, realm);

        // Step 3: Decode
        var (bytes, _) = DecodeBase64(str, alphabet, lastChunkHandling, realm);

        // Step 4: Create Uint8Array from decoded bytes
        var result = JsUint8Array.FromLength(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            result.SetElement(i, bytes[i]);
        }

        return (JsValue)result;
    }

    private static JsValue FromHex(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var input = args.GetArgument(0);

        if (!input.IsString)
        {
            throw ThrowTypeError("Uint8Array.fromHex requires a string argument", realm: realm);
        }

        var str = input.AsString();

        if (str.Length % 2 != 0)
        {
            throw ThrowSyntaxError("Invalid hex string: odd length", realm: realm);
        }

        var bytes = DecodeHex(str, realm);
        var result = JsUint8Array.FromLength(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            result.SetElement(i, bytes[i]);
        }

        return (JsValue)result;
    }

    private static JsValue ToBase64(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (!thisValue.TryGetObject<JsUint8Array>(out var typedArray))
        {
            throw ThrowTypeError("toBase64 called on non-Uint8Array", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw ThrowTypeError("Uint8Array is detached", realm: realm);
        }

        var options = args.GetArgument(0);
        var (alphabet, omitPadding) = ParseToBase64Options(options, realm);

        var bytes = GetBytes(typedArray);
        var encoded = EncodeBase64(bytes, alphabet, omitPadding);
        return (JsValue)encoded;
    }

    private static JsValue ToHex(JsValue thisValue, RealmState realm)
    {
        if (!thisValue.TryGetObject<JsUint8Array>(out var typedArray))
        {
            throw ThrowTypeError("toHex called on non-Uint8Array", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw ThrowTypeError("Uint8Array is detached", realm: realm);
        }

        var bytes = GetBytes(typedArray);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return (JsValue)sb.ToString();
    }

    private static JsValue SetFromBase64(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (!thisValue.TryGetObject<JsUint8Array>(out var typedArray))
        {
            throw ThrowTypeError("setFromBase64 called on non-Uint8Array", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw ThrowTypeError("Uint8Array is detached", realm: realm);
        }

        var input = args.GetArgument(0);
        if (!input.IsString)
        {
            throw ThrowTypeError("setFromBase64 requires a string argument", realm: realm);
        }

        var str = input.AsString();
        var options = args.GetArgument(1);
        var (alphabet, lastChunkHandling) = ParseBase64Options(options, realm);

        var (bytes, readLength) = DecodeBase64(str, alphabet, lastChunkHandling, realm);

        var written = Math.Min(bytes.Length, typedArray.Length);
        for (var i = 0; i < written; i++)
        {
            typedArray.SetElement(i, bytes[i]);
        }

        // Return { read, written } object
        var result = new JsObject();
        result.SetProperty("read", JsValue.FromNumber(readLength));
        result.SetProperty("written", JsValue.FromNumber(written));
        return (JsValue)result;
    }

    private static JsValue SetFromHex(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (!thisValue.TryGetObject<JsUint8Array>(out var typedArray))
        {
            throw ThrowTypeError("setFromHex called on non-Uint8Array", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw ThrowTypeError("Uint8Array is detached", realm: realm);
        }

        var input = args.GetArgument(0);
        if (!input.IsString)
        {
            throw ThrowTypeError("setFromHex requires a string argument", realm: realm);
        }

        var str = input.AsString();

        if (str.Length % 2 != 0)
        {
            throw ThrowSyntaxError("Invalid hex string: odd length", realm: realm);
        }

        var bytes = DecodeHex(str, realm);

        var written = Math.Min(bytes.Length, typedArray.Length);
        for (var i = 0; i < written; i++)
        {
            typedArray.SetElement(i, bytes[i]);
        }

        var result = new JsObject();
        result.SetProperty("read", JsValue.FromNumber(written * 2));
        result.SetProperty("written", JsValue.FromNumber(written));
        return (JsValue)result;
    }

    // ==================== Helpers ====================

    private static byte[] GetBytes(JsUint8Array typedArray)
    {
        var length = typedArray.Length;
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)typedArray.GetElement(i);
        }

        return bytes;
    }

    private static (string Alphabet, string LastChunkHandling) ParseBase64Options(JsValue options,
        RealmState realm)
    {
        var alphabet = "base64";
        var lastChunkHandling = "loose";

        if (options.IsUndefined || options.IsNull)
        {
            return (alphabet, lastChunkHandling);
        }

        if (!options.TryGetObject<IJsPropertyAccessor>(out var obj))
        {
            throw ThrowTypeError("Options must be an object", realm: realm);
        }

        if (obj.TryGetProperty("alphabet", out var alphaVal) && !alphaVal.IsUndefined)
        {
            alphabet = alphaVal.AsString();
            if (alphabet is not ("base64" or "base64url"))
            {
                throw ThrowTypeError(
                    $"Invalid alphabet: '{alphabet}'. Must be 'base64' or 'base64url'", realm: realm);
            }
        }

        if (obj.TryGetProperty("lastChunkHandling", out var lchVal) && !lchVal.IsUndefined)
        {
            lastChunkHandling = lchVal.AsString();
            if (lastChunkHandling is not ("loose" or "strict" or "stop-before-partial"))
            {
                throw ThrowTypeError(
                    $"Invalid lastChunkHandling: '{lastChunkHandling}'", realm: realm);
            }
        }

        return (alphabet, lastChunkHandling);
    }

    private static (string Alphabet, bool OmitPadding) ParseToBase64Options(JsValue options,
        RealmState realm)
    {
        var alphabet = "base64";
        var omitPadding = false;

        if (options.IsUndefined || options.IsNull)
        {
            return (alphabet, omitPadding);
        }

        if (!options.TryGetObject<IJsPropertyAccessor>(out var obj))
        {
            throw ThrowTypeError("Options must be an object", realm: realm);
        }

        if (obj.TryGetProperty("alphabet", out var alphaVal) && !alphaVal.IsUndefined)
        {
            alphabet = alphaVal.AsString();
            if (alphabet is not ("base64" or "base64url"))
            {
                throw ThrowTypeError(
                    $"Invalid alphabet: '{alphabet}'. Must be 'base64' or 'base64url'", realm: realm);
            }
        }

        if (obj.TryGetProperty("omitPadding", out var omitVal) && !omitVal.IsUndefined)
        {
            omitPadding = JsOps.ToBoolean(omitVal);
        }

        return (alphabet, omitPadding);
    }

    private static (byte[] Bytes, int ReadLength) DecodeBase64(string input, string alphabetName,
        string lastChunkHandling, RealmState realm)
    {
        var alphabet = alphabetName == "base64url" ? Base64UrlAlphabet : Base64Alphabet;
        var output = new List<byte>();
        var chunk = 0;
        var chunkBits = 0;
        var readLength = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            // Skip whitespace (ASCII whitespace: space, tab, newline, carriage return, form feed)
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                continue;
            }

            // Padding
            if (c == '=')
            {
                // In strict mode, padding must be correct
                if (chunkBits == 0)
                {
                    if (lastChunkHandling == "strict")
                    {
                        throw ThrowSyntaxError("Invalid base64: unexpected padding", realm: realm);
                    }
                }

                readLength = i + 1;

                // Expect remaining padding if needed
                if (chunkBits == 2)
                {
                    // Need == after 1 char of partial
                    // First = found, look for second =
                    if (i + 1 < input.Length && input[i + 1] == '=')
                    {
                        readLength = i + 2;
                    }
                    else if (lastChunkHandling == "strict")
                    {
                        throw ThrowSyntaxError("Invalid base64: missing padding", realm: realm);
                    }
                }

                // Skip remaining padding and whitespace
                break;
            }

            var value = alphabet.IndexOf(c);
            if (value == -1)
            {
                throw ThrowSyntaxError(
                    $"Invalid base64 character: '{c}'", realm: realm);
            }

            chunk = (chunk << 6) | value;
            chunkBits += 6;
            readLength = i + 1;

            if (chunkBits >= 8)
            {
                chunkBits -= 8;
                output.Add((byte)((chunk >> chunkBits) & 0xFF));
                chunk &= (1 << chunkBits) - 1;
            }
        }

        // Handle remaining bits
        if (chunkBits > 0)
        {
            if (lastChunkHandling == "strict")
            {
                throw ThrowSyntaxError("Invalid base64: incomplete chunk in strict mode",
                    realm: realm);
            }

            if (lastChunkHandling == "stop-before-partial")
            {
                // Back up readLength to before the partial chunk
                // A partial chunk is 1 char (6 bits, chunkBits==6) or depends on position
                // We need to find where the last complete group of 4 chars ended
                readLength -= chunkBits / 6;
                // Remove any bytes we might have added from the partial
                // Actually for stop-before-partial, if chunkBits < 6 we haven't added excess
                // The spec says: stop before the last chunk if it would be partial
                if (chunkBits == 2)
                {
                    readLength -= 1; // 1 char produced 6 bits, used none, back up 1
                }
                else if (chunkBits == 4)
                {
                    readLength -= 2; // 2 chars but didn't complete a byte
                }
            }
            // "loose": ignore remaining bits (already handled by not adding partial byte)
        }

        return (output.ToArray(), readLength);
    }

    private static string EncodeBase64(byte[] bytes, string alphabetName, bool omitPadding)
    {
        var alphabet = alphabetName == "base64url" ? Base64UrlAlphabet : Base64Alphabet;
        var sb = new StringBuilder((bytes.Length + 2) / 3 * 4);

        for (var i = 0; i < bytes.Length; i += 3)
        {
            var b0 = bytes[i];
            var b1 = i + 1 < bytes.Length ? bytes[i + 1] : 0;
            var b2 = i + 2 < bytes.Length ? bytes[i + 2] : 0;

            sb.Append(alphabet[(b0 >> 2) & 0x3F]);
            sb.Append(alphabet[((b0 & 0x03) << 4) | ((b1 >> 4) & 0x0F)]);

            if (i + 1 < bytes.Length)
            {
                sb.Append(alphabet[((b1 & 0x0F) << 2) | ((b2 >> 6) & 0x03)]);
            }
            else if (!omitPadding)
            {
                sb.Append('=');
            }

            if (i + 2 < bytes.Length)
            {
                sb.Append(alphabet[b2 & 0x3F]);
            }
            else if (!omitPadding)
            {
                sb.Append('=');
            }
        }

        return sb.ToString();
    }

    private static byte[] DecodeHex(string input, RealmState realm)
    {
        var bytes = new byte[input.Length / 2];
        for (var i = 0; i < input.Length; i += 2)
        {
            var hi = HexDigitValue(input[i]);
            var lo = HexDigitValue(input[i + 1]);
            if (hi < 0 || lo < 0)
            {
                throw ThrowSyntaxError(
                    $"Invalid hex character at position {i}", realm: realm);
            }

            bytes[i / 2] = (byte)((hi << 4) | lo);
        }

        return bytes;
    }

    private static int HexDigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };
}

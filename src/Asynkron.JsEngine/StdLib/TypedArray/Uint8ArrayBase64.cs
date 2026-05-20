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

    public static void Register(HostFunction constructor, RealmState realm)
    {
        if (constructor.TryGetProperty("prototype", out var protoVal) &&
            protoVal.TryGetObject<IJsPropertyAccessor>(out var protoObj))
        {
            realm.Uint8ArrayPrototype = protoObj;
        }

        AddMethod(constructor, "fromBase64", (_, args) => FromBase64(args, realm), 1, realm);
        AddMethod(constructor, "fromHex", (_, args) => FromHex(args, realm), 1, realm);

        if (protoVal.TryGetObject<JsObject>(out var prototype))
        {
            AddMethod(prototype, "toBase64", (thisVal, args) => ToBase64(thisVal, args, realm), 0, realm);
            AddMethod(prototype, "toHex", (thisVal, _) => ToHex(thisVal, realm), 0, realm);
            AddMethod(prototype, "setFromBase64",
                (thisVal, args) => SetFromBase64(thisVal, args, realm), 1, realm);
            AddMethod(prototype, "setFromHex",
                (thisVal, args) => SetFromHex(thisVal, args, realm), 1, realm);
        }
    }

    private static void AddMethod(IPropertyDefinitionHost target, string name, JsHostHandler handler,
        int length, RealmState realm)
    {
        var fn = new HostFunction(handler, realm, false);
        fn.DefineProperty("name", new PropertyDescriptor
        {
            Value = (JsValue)name,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        fn.DefineProperty("length", new PropertyDescriptor
        {
            Value = JsValue.FromDouble(length),
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        target.TryDefineProperty(name, new PropertyDescriptor
        {
            Value = (JsValue)fn,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
    }

    private static JsValue FromBase64(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var input = args.GetArgument(0);
        if (!input.IsString)
        {
            throw ThrowTypeError("Uint8Array.fromBase64 requires a string argument", realm: realm);
        }

        var (alphabet, lastChunkHandling) = ParseBase64Options(args.GetArgument(1), realm);
        var result = FromBase64Core(input.AsString(), alphabet, lastChunkHandling, int.MaxValue, realm);
        if (result.Error is not null)
        {
            throw result.Error;
        }

        return (JsValue)CreateUint8Array(result.Bytes, realm);
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

        return (JsValue)CreateUint8Array(DecodeHexFull(str, realm), realm);
    }

    private static JsValue ToBase64(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState realm)
    {
        if (!thisValue.TryGetObject<JsUint8Array>(out var typedArray))
        {
            throw ThrowTypeError("toBase64 called on non-Uint8Array", realm: realm);
        }

        var (alphabet, omitPadding) = ParseToBase64Options(args.GetArgument(0), realm);

        // Check detached AFTER reading options (getter may detach buffer)
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw ThrowTypeError("Uint8Array is detached", realm: realm);
        }

        return (JsValue)EncodeBase64(GetBytes(typedArray), alphabet, omitPadding);
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

        var input = args.GetArgument(0);
        if (!input.IsString)
        {
            throw ThrowTypeError("setFromBase64 requires a string argument", realm: realm);
        }

        var (alphabet, lastChunkHandling) = ParseBase64Options(args.GetArgument(1), realm);

        // Check detached AFTER reading options (getter may detach buffer)
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw ThrowTypeError("Uint8Array is detached", realm: realm);
        }

        var dr = FromBase64Core(input.AsString(), alphabet, lastChunkHandling, typedArray.Length, realm);
        var written = Math.Min(dr.Bytes.Length, typedArray.Length);
        for (var i = 0; i < written; i++)
        {
            typedArray.SetElement(i, dr.Bytes[i]);
        }

        if (dr.Error is not null)
        {
            throw dr.Error;
        }

        var result = new JsObject();
        result.SetProperty("read", JsValue.FromNumber(dr.Read));
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

        var hr = FromHexCore(input.AsString(), typedArray.Length, realm);
        for (var i = 0; i < hr.Written; i++)
        {
            typedArray.SetElement(i, hr.Bytes[i]);
        }

        if (hr.Error is not null)
        {
            throw hr.Error;
        }

        var result = new JsObject();
        result.SetProperty("read", JsValue.FromNumber(hr.Read));
        result.SetProperty("written", JsValue.FromNumber(hr.Written));
        return (JsValue)result;
    }

    // ==================== Helpers ====================

    private static JsUint8Array CreateUint8Array(byte[] bytes, RealmState realm)
    {
        var result = JsUint8Array.FromLength(bytes.Length, realm);
        for (var i = 0; i < bytes.Length; i++)
        {
            result.SetElement(i, bytes[i]);
        }

        if (realm.Uint8ArrayPrototype is not null)
        {
            result.SetPrototype(realm.Uint8ArrayPrototype);
        }

        return result;
    }

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

    private static (string Alphabet, string LastChunkHandling) ParseBase64Options(
        JsValue options, RealmState realm)
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
            if (!alphaVal.IsString)
            {
                throw ThrowTypeError("alphabet option must be a string", realm: realm);
            }

            alphabet = alphaVal.AsString();
            if (alphabet is not ("base64" or "base64url"))
            {
                throw ThrowTypeError(
                    $"Invalid alphabet: '{alphabet}'. Must be 'base64' or 'base64url'", realm: realm);
            }
        }

        if (obj.TryGetProperty("lastChunkHandling", out var lchVal) && !lchVal.IsUndefined)
        {
            if (!lchVal.IsString)
            {
                throw ThrowTypeError("lastChunkHandling option must be a string", realm: realm);
            }

            lastChunkHandling = lchVal.AsString();
            if (lastChunkHandling is not ("loose" or "strict" or "stop-before-partial"))
            {
                throw ThrowTypeError(
                    $"Invalid lastChunkHandling: '{lastChunkHandling}'", realm: realm);
            }
        }

        return (alphabet, lastChunkHandling);
    }

    private static (string Alphabet, bool OmitPadding) ParseToBase64Options(
        JsValue options, RealmState realm)
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
            if (!alphaVal.IsString)
            {
                throw ThrowTypeError("alphabet option must be a string", realm: realm);
            }

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

    // ==================== Decode Result ====================

    private readonly struct DecodeResult
    {
        public byte[] Bytes { get; init; }
        public int Read { get; init; }
        public int Written { get; init; }
        public Exception? Error { get; init; }
    }

    // ==================== Base64 Core Decode ====================

    private static DecodeResult FromBase64Core(string input, string alphabetName,
        string lastChunkHandling, int maxLen, RealmState realm)
    {
        var alpha = alphabetName == "base64url" ? Base64UrlAlphabet : Base64Alphabet;
        var output = new List<byte>();
        var chunk = 0;
        var cl = 0; // chars in current 4-char group
        var lcge = 0; // last complete group end position in input

        for (var i = 0; i < input.Length; i++)
        {
            if (output.Count >= maxLen)
            {
                return Ok(output, lcge);
            }

            var c = input[i];
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                continue;
            }

            if (c == '=')
            {
                if (cl is 0 or 1)
                {
                    return Err(output, lcge, "Invalid base64: unexpected padding", realm);
                }

                if (cl == 2)
                {
                    return HandlePadding2(input, i, output, chunk, lcge, lastChunkHandling, maxLen, realm);
                }

                if (cl == 3)
                {
                    return HandlePadding3(input, i, output, chunk, lcge, lastChunkHandling, maxLen, realm);
                }

                return Err(output, lcge, "Invalid base64: unexpected state", realm);
            }

            var v = alpha.IndexOf(c);
            if (v == -1)
            {
                return Err(output, lcge, $"Invalid base64 character: '{c}'", realm);
            }

            chunk = (chunk << 6) | v;
            cl++;

            if (cl == 4)
            {
                var remaining = maxLen - output.Count;
                if (remaining >= 3)
                {
                    output.Add((byte)((chunk >> 16) & 0xFF));
                    output.Add((byte)((chunk >> 8) & 0xFF));
                    output.Add((byte)(chunk & 0xFF));
                    chunk = 0;
                    cl = 0;
                    lcge = i + 1;
                }
                else
                {
                    return Ok(output, lcge);
                }
            }
        }

        // End of input - handle partial chunks
        if (cl == 0)
        {
            return Ok(output, input.Length);
        }

        if (cl == 1)
        {
            if (lastChunkHandling == "stop-before-partial")
            {
                return Ok(output, lcge);
            }

            return Err(output, lcge, "Invalid base64: incomplete chunk (single character)", realm);
        }

        // cl is 2 or 3
        if (lastChunkHandling == "stop-before-partial")
        {
            return Ok(output, lcge);
        }

        if (lastChunkHandling == "strict")
        {
            return Err(output, lcge, "Invalid base64: incomplete chunk in strict mode", realm);
        }

        // loose: decode partial
        if (cl == 2)
        {
            if (output.Count < maxLen)
            {
                output.Add((byte)((chunk >> 4) & 0xFF));
            }
        }
        else // cl == 3
        {
            var remaining = maxLen - output.Count;
            if (remaining >= 2)
            {
                output.Add((byte)((chunk >> 10) & 0xFF));
                output.Add((byte)((chunk >> 2) & 0xFF));
            }
            else
            {
                return Ok(output, lcge);
            }
        }

        return Ok(output, input.Length);
    }

    /// <summary>Handle '=' padding when chunkLength == 2 (expects "==").</summary>
    private static DecodeResult HandlePadding2(string input, int eqPos, List<byte> output,
        int chunk, int lcge, string lastChunkHandling, int maxLen, RealmState realm)
    {
        // Find second '='
        var foundSecond = false;
        var j = eqPos + 1;
        for (; j < input.Length; j++)
        {
            var c2 = input[j];
            if (c2 is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                continue;
            }

            if (c2 == '=') { foundSecond = true; j++; break; }
            return Err(output, lcge, "Invalid base64: expected '=' padding", realm);
        }

        if (!foundSecond)
        {
            if (lastChunkHandling == "stop-before-partial")
            {
                return Ok(output, lcge);
            }

            return Err(output, lcge, "Invalid base64: incomplete padding", realm);
        }

        // Check for trailing non-whitespace
        for (; j < input.Length; j++)
        {
            if (input[j] is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                continue;
            }

            return Err(output, lcge, "Invalid base64: trailing characters after padding", realm);
        }

        if (lastChunkHandling == "strict" && (chunk & 0xF) != 0)
        {
            return Err(output, lcge, "Invalid base64: non-zero padding bits in strict mode", realm);
        }

        // 2 data chars + "==" -> 1 byte
        if (output.Count < maxLen)
        {
            output.Add((byte)((chunk >> 4) & 0xFF));
        }

        return Ok(output, j);
    }

    /// <summary>Handle '=' padding when chunkLength == 3 (expects single "=").</summary>
    private static DecodeResult HandlePadding3(string input, int eqPos, List<byte> output,
        int chunk, int lcge, string lastChunkHandling, int maxLen, RealmState realm)
    {
        var j = eqPos + 1;
        for (; j < input.Length; j++)
        {
            var trailing = input[j];
            if (trailing is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                continue;
            }

            if (trailing == '=')
            {
                return Err(output, lcge, "Invalid base64: excess padding", realm);
            }

            return Err(output, lcge, "Invalid base64: trailing characters after padding", realm);
        }

        if (lastChunkHandling == "strict" && (chunk & 0x3) != 0)
        {
            return Err(output, lcge, "Invalid base64: non-zero padding bits in strict mode", realm);
        }

        // 3 data chars + "=" -> 2 bytes
        var remaining = maxLen - output.Count;
        if (remaining >= 2)
        {
            output.Add((byte)((chunk >> 10) & 0xFF));
            output.Add((byte)((chunk >> 2) & 0xFF));
        }
        else
        {
            return Ok(output, lcge);
        }

        return Ok(output, j);
    }

    private static DecodeResult Ok(List<byte> output, int read) =>
        new() { Bytes = output.ToArray(), Read = read, Written = output.Count, Error = null };

    private static DecodeResult Err(List<byte> output, int lcgr, string msg, RealmState realm) =>
        new()
        {
            Bytes = output.ToArray(),
            Read = lcgr,
            Written = output.Count,
            Error = ThrowSyntaxError(msg, realm: realm)
        };

    // ==================== Base64 Encode ====================

    private static string EncodeBase64(byte[] bytes, string alphabetName, bool omitPadding)
    {
        var alpha = alphabetName == "base64url" ? Base64UrlAlphabet : Base64Alphabet;
        var sb = new StringBuilder((bytes.Length + 2) / 3 * 4);

        for (var i = 0; i < bytes.Length; i += 3)
        {
            var b0 = bytes[i];
            var b1 = i + 1 < bytes.Length ? bytes[i + 1] : 0;
            var b2 = i + 2 < bytes.Length ? bytes[i + 2] : 0;

            sb.Append(alpha[(b0 >> 2) & 0x3F]);
            sb.Append(alpha[((b0 & 0x03) << 4) | ((b1 >> 4) & 0x0F)]);

            if (i + 1 < bytes.Length)
            {
                sb.Append(alpha[((b1 & 0x0F) << 2) | ((b2 >> 6) & 0x03)]);
            }
            else if (!omitPadding)
            {
                sb.Append('=');
            }

            if (i + 2 < bytes.Length)
            {
                sb.Append(alpha[b2 & 0x3F]);
            }
            else if (!omitPadding)
            {
                sb.Append('=');
            }
        }

        return sb.ToString();
    }

    // ==================== Hex Decode ====================

    /// <summary>Incremental hex decode for setFromHex (writes-up-to-error behavior).</summary>
    private static DecodeResult FromHexCore(string input, int maxLen, RealmState realm)
    {
        var bytes = new List<byte>();
        var read = 0;

        if (input.Length % 2 != 0)
        {
            return new DecodeResult
            {
                Bytes = [],
                Read = 0,
                Written = 0,
                Error = ThrowSyntaxError("Invalid hex string: odd length", realm: realm)
            };
        }

        for (var i = 0; i < input.Length; i += 2)
        {
            if (bytes.Count >= maxLen)
            {
                break;
            }

            var hi = HexDigitValue(input[i]);
            var lo = HexDigitValue(input[i + 1]);
            if (hi < 0 || lo < 0)
            {
                return new DecodeResult
                {
                    Bytes = bytes.ToArray(),
                    Read = i,
                    Written = bytes.Count,
                    Error = ThrowSyntaxError(
                        $"Invalid hex character at position {(hi < 0 ? i : i + 1)}", realm: realm)
                };
            }

            bytes.Add((byte)((hi << 4) | lo));
            read = i + 2;
        }

        return new DecodeResult
        {
            Bytes = bytes.ToArray(),
            Read = read,
            Written = bytes.Count,
            Error = null
        };
    }

    /// <summary>Full hex decode for fromHex (no maxLength, immediate throws).</summary>
    private static byte[] DecodeHexFull(string input, RealmState realm)
    {
        var bytes = new byte[input.Length / 2];
        for (var i = 0; i < input.Length; i += 2)
        {
            var hi = HexDigitValue(input[i]);
            var lo = HexDigitValue(input[i + 1]);
            if (hi < 0 || lo < 0)
            {
                throw ThrowSyntaxError($"Invalid hex character at position {i}", realm: realm);
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

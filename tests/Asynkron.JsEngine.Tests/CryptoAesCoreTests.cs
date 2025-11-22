using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class CryptoAesCoreTests
{
    [Fact(Timeout = 10000)]
    public async Task Cipher_CoreBlock_Matches_Node_Result()
    {
        // Expected hex from running the crypto-aes.js Cipher/KeyExpansion in Node
        // with the AES-128 FIPS-197 test vector, using byteArrayToHexStr as defined
        // in the script (no zero-padding).
        const string expectedHex = "3925841d2dc9fbdc118597196ab32";

        var script = SunSpiderTests.GetEmbeddedFile("crypto-aes.js");

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        const string coreTest = @"
            var __aesCoreHex = (function () {
                var keyBytes = [0x2b,0x7e,0x15,0x16,0x28,0xae,0xd2,0xa6,0xab,0xf7,0x15,0x88,0x09,0xcf,0x4f,0x3c];
                var input    = [0x32,0x43,0xf6,0xa8,0x88,0x5a,0x30,0x8d,0x31,0x31,0x98,0xa2,0xe0,0x37,0x07,0x34];
                var w = KeyExpansion(keyBytes);
                var out = Cipher(input, w);
                return byteArrayToHexStr(out).replace(/\s+/g, '');
            })();
        ";

        await engine.Evaluate(coreTest);
        var hex = await engine.Evaluate("__aesCoreHex;") as string;

        Assert.Equal(expectedHex, hex);
    }

    [Fact(Timeout = 10000)]
    public async Task Ctr_Roundtrip_ShortPlaintext_Works()
    {
        var script = SunSpiderTests.GetEmbeddedFile("crypto-aes.js");

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        // Stabilise the nonce used by AESEncryptCtr so the roundtrip is deterministic.
        await engine.Evaluate(@"
            Date = function() {
                this.getTime = function() { return 0; };
            };
        ");

        const string ctrTest = @"
            var __ctrPlain = 'HELLO AES CTR';
            var __ctrCipher = AESEncryptCtr(__ctrPlain, 'secret-password', 128);
            var __ctrDecrypted = AESDecryptCtr(__ctrCipher, 'secret-password', 128);
        ";

        await engine.Evaluate(ctrTest);

        var plain = await engine.Evaluate("__ctrPlain;") as string;
        var decrypted = await engine.Evaluate("__ctrDecrypted;") as string;

        Assert.Equal(plain, decrypted);
    }
}

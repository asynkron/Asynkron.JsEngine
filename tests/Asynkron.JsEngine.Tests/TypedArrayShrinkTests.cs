using System.Threading.Tasks;
using Asynkron.JsEngine.JsTypes;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedArrayShrinkTests
{
    [Theory]
    [InlineData("Int8Array", 1)]
    [InlineData("Int16Array", 2)]
    [InlineData("Int32Array", 4)]
    [InlineData("BigInt64Array", 8)]
    public async Task FixedLengthViewGoesOobAfterResize(string ctor, int bpe)
    {
        await using var engine = new JsEngine();
        var script = $@"
            (function() {{
              const rab = new ArrayBuffer({4 * bpe}, {{ maxByteLength: {8 * bpe} }});
              const fixed = new {ctor}(rab, 0, 4);
              let evil = {{
                valueOf() {{
                  rab.resize({2 * bpe});
                  return 2;
                }}
              }};
              const beforeLen = fixed.length;
              const beforeBytes = rab.byteLength;
              const res = Array.prototype.lastIndexOf.call(fixed, fixed[0], evil);
              return {{
                beforeLen,
                beforeBytes,
                afterLen: fixed.length,
                afterBytes: rab.byteLength,
                res
              }};
            }})();
        ";

        var result = await engine.Evaluate(script);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(4d, obj["beforeLen"]);
        Assert.Equal((double)(4 * bpe), obj["beforeBytes"]);
        Assert.Equal(-1d, obj["res"]);
        Assert.Equal(0d, obj["afterLen"]);
        Assert.Equal((double)(2 * bpe), obj["afterBytes"]);
    }

    [Theory]
    [InlineData("Int8Array", 1, "2")]
    [InlineData("BigInt64Array", 8, "BigInt(2)")]
    public async Task LengthTrackingViewShrinksDuringFromIndex(string ctor, int bpe, string targetValue)
    {
        await using var engine = new JsEngine();
        var script = $@"
            (function() {{
              const rab = new ArrayBuffer({4 * bpe}, {{ maxByteLength: {8 * bpe} }});
              const tracking = new {ctor}(rab);
              for (let i = 0; i < tracking.length; ++i) {{
                tracking[i] = {targetValue.Replace("2", "i")};
              }}
              const target = tracking[2];
              const evil = {{
                valueOf() {{
                  rab.resize({2 * bpe});
                  return 2;
                }}
              }};
              const before = tracking.length;
              const res = Array.prototype.lastIndexOf.call(tracking, target, evil);
              return {{
                before,
                after: tracking.length,
                res
              }};
            }})();
        ";

        var result = await engine.Evaluate(script);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(4d, obj["before"]);
        Assert.Equal(2d, obj["after"]);
        Assert.Equal(-1d, obj["res"]);
    }

    [Fact]
    public async Task LastIndexOfThrowsWhenViewIsAlreadyOutOfBounds()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const rab = new ArrayBuffer(16, { maxByteLength: 24 });
              const view = new Int8Array(rab, 8, 4);
              rab.resize(11);
              try {
                view.lastIndexOf(0);
                return false;
              } catch (e) {
                return e instanceof TypeError;
              }
            })();
        """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task LastIndexOfDoesNotCoerceFromIndexWhenLengthIsZero()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const ta = new Int8Array();
              const evil = { valueOf() { throw new Error("should not be called"); } };
              try {
                const res = ta.lastIndexOf(0, evil);
                return { res, length: ta.length };
              } catch (e) {
                return { threw: true };
              }
            })();
        """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(0d, obj["length"]);
        Assert.Equal(-1d, obj["res"]);
        Assert.False(obj.TryGetValue("threw", out var threw) && threw is bool b && b);
    }
}

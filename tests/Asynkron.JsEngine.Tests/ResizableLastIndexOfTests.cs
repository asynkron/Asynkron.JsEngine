using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ResizableLastIndexOfTests
{
    [Fact]
    public async Task ArrayPrototypeLastIndexOfUsesPreCoercionLengthWhenBufferGrows()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const rab = new ArrayBuffer(4, { maxByteLength: 8 });
              const ta = new Int8Array(rab);
              for (let i = 0; i < 4; ++i) ta[i] = 1;
              const evil = { valueOf() { rab.resize(6); return -1; } };
              return Array.prototype.lastIndexOf.call(ta, 0, evil);
            })();
        """);

        Assert.Equal(-1d, result);
    }

    [Fact]
    public async Task TypedArrayLastIndexOfThrowsOnDetachedBuffer()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const ta = new Int8Array(1);
              $DETACHBUFFER(ta.buffer);
              try {
                ta.lastIndexOf(0);
                return "no-throw";
              } catch (e) {
                return e instanceof TypeError;
              }
            })();
        """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task BigIntTypedArrayLastIndexOfThrowsOnDetachedBuffer()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const ta = new BigInt64Array(1);
              $DETACHBUFFER(ta.buffer);
              try {
                ta.lastIndexOf(0n);
                return "no-throw";
              } catch (e) {
                return e instanceof TypeError;
              }
            })();
        """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task TypedArrayLastIndexOfThrowsOnNonObjectThis()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const fn = TypedArray.prototype.lastIndexOf;
              try {
                fn.call(undefined, 1);
                return "no-throw";
              } catch (e) {
                return e instanceof TypeError;
              }
            })();
        """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task TypedArrayLastIndexOfThrowsOnNonTypedArrayReceiver()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              const fn = TypedArray.prototype.lastIndexOf;
              try {
                fn.call({}, 1);
                return "no-throw";
              } catch (e) {
                return e instanceof TypeError;
              }
            })();
        """);

        Assert.Equal(true, result);
    }
}

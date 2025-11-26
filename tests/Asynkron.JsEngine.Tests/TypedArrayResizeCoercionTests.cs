using System.Threading.Tasks;
using Asynkron.JsEngine.JsTypes;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedArrayResizeCoercionTests
{
    [Fact]
    public async Task ValueOfCoercionCanResizeBackingBuffer()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            (function() {
              const rab = new ArrayBuffer(4, { maxByteLength: 8 });
              const ta = new Int8Array(rab, 0, 4);
              const evil = { valueOf() { rab.resize(2); return 2; } };
              const beforeLen = ta.length;
              const beforeBytes = rab.byteLength;
              const hasResize = typeof rab.resize === 'function';
              const resizable = rab.resizable;
              const result = Array.prototype.lastIndexOf.call(ta, 0, evil);
              return {
                beforeLen,
                beforeBytes,
                hasResize,
                resizable,
                afterLen: ta.length,
                afterBytes: rab.byteLength,
                result
              };
            })();
        ");

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(4d, obj["beforeLen"]);
        Assert.Equal(4d, obj["beforeBytes"]);
        Assert.True(obj["hasResize"] as bool?);
        Assert.True(obj["resizable"] as bool?);

        // After resize the view is OOB, so length should be 0 and lastIndexOf should return -1.
        Assert.Equal(0d, obj["afterLen"]);
        Assert.Equal(2d, obj["afterBytes"]);
        Assert.Equal(-1d, obj["result"]);
    }
}

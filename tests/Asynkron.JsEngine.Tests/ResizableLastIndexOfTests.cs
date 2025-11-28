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

    [Fact]
    public async Task ArrayPrototypeLastIndexOfLengthTrackingSubclassIgnoresGrowth()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              class MyInt8Array extends Int8Array {}
              const rab = new ArrayBuffer(4, { maxByteLength: 8 });
              const ta = new MyInt8Array(rab);
              for (let i = 0; i < 4; ++i) ta[i] = 1;
              const evil = { valueOf() { rab.resize(6); return -1; } };
              return Array.prototype.lastIndexOf.call(ta, 0, evil);
            })();
        """);

        Assert.Equal(-1d, result);
    }

    [Fact]
    public async Task TypedArrayLastIndexOfLengthTrackingSubclassIgnoresGrowth()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              class MyInt8Array extends Int8Array {}
              const rab = new ArrayBuffer(4, { maxByteLength: 8 });
              const ta = new MyInt8Array(rab);
              for (let i = 0; i < 4; ++i) ta[i] = 1;
              const evil = { valueOf() { rab.resize(6); return -1; } };
              return ta.lastIndexOf(0, evil);
            })();
        """);

        Assert.Equal(-1d, result);
    }

    [Fact]
    public async Task ArrayPrototypeLastIndexOfResizableGrowthMatchesTest262Ctors()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              function subClass(type) { try { return new Function('return class My' + type + ' extends ' + type + ' {}')(); } catch (e) {} }
              const builtinCtors = [Uint8Array, Int8Array, Uint16Array, Int16Array, Uint32Array, Int32Array, Float32Array, Float64Array, Uint8ClampedArray];
              if (typeof BigUint64Array !== 'undefined') builtinCtors.push(BigUint64Array);
              if (typeof BigInt64Array !== 'undefined') builtinCtors.push(BigInt64Array);
              const ctors = builtinCtors.filter(c => typeof c !== 'undefined');

              function MayNeedBigInt(ta, n) {
                if ((typeof BigInt64Array !== 'undefined' && ta instanceof BigInt64Array) ||
                    (typeof BigUint64Array !== 'undefined' && ta instanceof BigUint64Array) ||
                    (typeof MyBigInt64Array !== 'undefined' && ta instanceof MyBigInt64Array)) {
                  return BigInt(n);
                }
                return n;
              }

              const failures = [];

              for (const ctor of ctors) {
                // Block 1: filled with ones, search for 0 => -1 even after growth.
                {
                  const rab = new ArrayBuffer(4 * ctor.BYTES_PER_ELEMENT, { maxByteLength: 8 * ctor.BYTES_PER_ELEMENT });
                  const ta = new ctor(rab);
                  for (let i = 0; i < 4; ++i) ta[i] = MayNeedBigInt(ta, 1);
                  const evil = { valueOf() { rab.resize(6 * ctor.BYTES_PER_ELEMENT); return -1; } };
                  const n0 = MayNeedBigInt(ta, 0);
                  if (Array.prototype.lastIndexOf.call(ta, n0) !== -1) {
                    failures.push({ ctor: ctor.name, phase: "array-ones-base" });
                  }
                  if (Array.prototype.lastIndexOf.call(ta, n0, evil) !== -1) {
                    failures.push({ ctor: ctor.name, phase: "array-ones-evil" });
                  }
                }

                // Block 2: default zeros, fromIndex conversion uses original length.
                {
                  const rab = new ArrayBuffer(4 * ctor.BYTES_PER_ELEMENT, { maxByteLength: 8 * ctor.BYTES_PER_ELEMENT });
                  const ta = new ctor(rab);
                  const evil = { valueOf() { rab.resize(6 * ctor.BYTES_PER_ELEMENT); return -4; } };
                  const n0 = MayNeedBigInt(ta, 0);
                  const baseRes = Array.prototype.lastIndexOf.call(ta, n0, -4);
                  if (baseRes !== 0) {
                    failures.push({ ctor: ctor.name ?? "<anon>", phase: "array-zeros-base", got: baseRes });
                  }
                  const evilRes = Array.prototype.lastIndexOf.call(ta, n0, evil);
                  if (evilRes !== 0) {
                    failures.push({ ctor: ctor.name ?? "<anon>", phase: "array-zeros-evil", got: evilRes });
                  }
                }
              }

              return failures.length === 0 ? "ok" : JSON.stringify(failures);
            })();
        """);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task TypedArrayLastIndexOfResizableGrowthMatchesTest262Ctors()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
              function subClass(type) { try { return new Function('return class My' + type + ' extends ' + type + ' {}')(); } catch (e) {} }
              const builtinCtors = [Uint8Array, Int8Array, Uint16Array, Int16Array, Uint32Array, Int32Array, Float32Array, Float64Array, Uint8ClampedArray];
              if (typeof BigUint64Array !== 'undefined') builtinCtors.push(BigUint64Array);
              if (typeof BigInt64Array !== 'undefined') builtinCtors.push(BigInt64Array);
              const ctors = builtinCtors.filter(c => typeof c !== 'undefined');

              function MayNeedBigInt(ta, n) {
                if ((typeof BigInt64Array !== 'undefined' && ta instanceof BigInt64Array) ||
                    (typeof BigUint64Array !== 'undefined' && ta instanceof BigUint64Array) ||
                    (typeof MyBigInt64Array !== 'undefined' && ta instanceof MyBigInt64Array)) {
                  return BigInt(n);
                }
                return n;
              }

              const failures = [];

              for (const ctor of ctors) {
                // Block 1: filled with ones, search for 0 => -1 even after growth.
                {
                  const rab = new ArrayBuffer(4 * ctor.BYTES_PER_ELEMENT, { maxByteLength: 8 * ctor.BYTES_PER_ELEMENT });
                  const ta = new ctor(rab);
                  for (let i = 0; i < 4; ++i) ta[i] = MayNeedBigInt(ta, 1);
                  const evil = { valueOf() { rab.resize(6 * ctor.BYTES_PER_ELEMENT); return -1; } };
                  const n0 = MayNeedBigInt(ta, 0);
                  if (ta.lastIndexOf(n0) !== -1) {
                    failures.push({ ctor: ctor.name, phase: "typed-ones-base" });
                  }
                  if (ta.lastIndexOf(n0, evil) !== -1) {
                    failures.push({ ctor: ctor.name, phase: "typed-ones-evil" });
                  }
                }

                // Block 2: default zeros, fromIndex conversion uses original length.
                {
                  const rab = new ArrayBuffer(4 * ctor.BYTES_PER_ELEMENT, { maxByteLength: 8 * ctor.BYTES_PER_ELEMENT });
                  const ta = new ctor(rab);
                  const evil = { valueOf() { rab.resize(6 * ctor.BYTES_PER_ELEMENT); return -4; } };
                  const n0 = MayNeedBigInt(ta, 0);
                  const baseRes = ta.lastIndexOf(n0, -4);
                  if (baseRes !== 0) {
                    failures.push({ ctor: ctor.name ?? "<anon>", phase: "typed-zeros-base", got: baseRes });
                  }
                  const evilRes = ta.lastIndexOf(n0, evil);
                  if (evilRes !== 0) {
                    failures.push({ ctor: ctor.name ?? "<anon>", phase: "typed-zeros-evil", got: evilRes });
                  }
                }
              }

              return failures.length === 0 ? "ok" : JSON.stringify(failures);
            })();
        """);

        Assert.Equal("ok", result);
    }
}

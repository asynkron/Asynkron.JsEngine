using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibTypedArray)]
public sealed class TypedArrayResizableTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void LastIndexOfReturnsMinusOneWhenFixedLengthViewShrinksOutOfBounds()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(4, 8, realm); // resizable buffer
        var fixedLength = new JsInt8Array(rab, 0, 4);

        var beforeShrink = TypedArrayBase.LastIndexOfInternal(fixedLength, new List<JsValue> { new JsValue(0d) });
        Assert.Equal(3d, beforeShrink);

        rab.Resize(2);

        Assert.Throws<ThrowSignal>(() =>
            TypedArrayBase.LastIndexOfInternal(fixedLength, new List<JsValue> { new JsValue(0d), new JsValue(2d) }));
    }

    [Fact]
    public void BigIntLastIndexOfReturnsMinusOneWhenFixedLengthViewShrinksOutOfBounds()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(32, 64, realm);
        var fixedLength = new JsBigInt64Array(rab, 0, 4);
        fixedLength.SetElement(0, new JsBigInt(0));

        var beforeShrink =
            TypedArrayBase.LastIndexOfInternal(fixedLength, new List<JsValue> { new JsValue(new JsBigInt(0)) });
        Assert.Equal(3d, beforeShrink);

        rab.Resize(16);

        Assert.Throws<ThrowSignal>(() =>
            TypedArrayBase.LastIndexOfInternal(fixedLength, new List<JsValue> { new JsValue(new JsBigInt(0)), new JsValue(2d) }));
    }

    [Fact]
    public void FixedLengthViewThrowsWhenResizableBufferShrinksOutOfBoundsDuringIteration()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(10, 20, realm);
        var typedArray = new JsUint8Array(rab, 0, 3);
        for (var i = 0; i < 3; i++)
        {
            typedArray.SetElement(i, i);
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 1,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.True(threw);
        Assert.Equal([0d, 1d], values);
    }

    [Fact]
    public void FixedLengthViewIteratesExactLengthOnResizableBuffer()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(10, 20, realm);
        var typedArray = new JsUint8Array(rab, 0, 3);
        for (var i = 0; i < 3; i++)
        {
            typedArray.SetElement(i, i);
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: int.MaxValue, newByteLength: 10,
            out var resized, out var threw);

        Assert.False(resized);
        Assert.False(threw);
        Assert.Equal([0d, 1d, 2d], values);
    }

    [Fact]
    public void FixedLengthViewWithOffsetKeepsValuesAfterInBoundsShrink()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(10, 20, realm);
        var writer = new JsUint8Array(rab, 0, 10);
        for (var i = 0; i < writer.Length; i++)
        {
            writer.SetElement(i, i);
        }

        var typedArray = new JsUint8Array(rab, 2, 3);
        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 5,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.False(threw);
        Assert.Equal([2d, 3d, 4d], values);
    }

    [Fact]
    public void LengthTrackingViewShrinksToNewLengthDuringIteration()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(10, 20, realm);
        var typedArray = new JsUint8Array(rab, 0, length: 10, isLengthTracking: true);
        for (var i = 0; i < 10; i++)
        {
            typedArray.SetElement(i, i);
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 5,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.False(threw);
        Assert.Equal([0d, 1d, 2d, 3d, 4d], values);
    }

    [Fact]
    public void LengthTrackingViewThrowsWhenResizeShrinksPastOffsetDuringIteration()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(10, 20, realm);
        var typedArray = new JsUint8Array(rab, byteOffset: 2, length: 8, isLengthTracking: true);
        for (var i = 0; i < 8; i++)
        {
            typedArray.SetElement(i, i + 2);
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 1,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.True(threw);
        Assert.Equal([2d, 3d], values);
    }

    [Fact]
    public void LengthTrackingViewGrowsDuringIterationAndReadsZeroes()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(10, 20, realm);
        var typedArray = new JsUint8Array(rab, 0, length: 10, isLengthTracking: true);
        for (var i = 0; i < 10; i++)
        {
            typedArray.SetElement(i, i);
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 20,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.False(threw);
        Assert.Equal(20, values.Count);
        Assert.Equal([0d, 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 9d], values.Take(10).ToArray());
        Assert.Equal(new double[10], values.Skip(10).ToArray());
    }

    [Fact]
    public void BigIntFixedLengthViewThrowsWhenResizableBufferShrinksOutOfBoundsDuringIteration()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(24, 48, realm);
        var typedArray = new JsBigInt64Array(rab, 0, 3);
        for (var i = 0; i < 3; i++)
        {
            typedArray.SetElement(i, new JsBigInt(i));
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 1,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.True(threw);
        Assert.Equal([0d, 1d], values);
    }

    [Fact]
    public void BigIntLengthTrackingViewShrinksToNewLengthDuringIteration()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(80, 160, realm);
        var typedArray = new JsBigInt64Array(rab, 0, length: 10, isLengthTracking: true);
        for (var i = 0; i < 10; i++)
        {
            typedArray.SetElement(i, new JsBigInt(i));
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 40,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.False(threw);
        Assert.Equal([0d, 1d, 2d, 3d, 4d], values);
    }

    [Fact]
    public void BigIntLengthTrackingViewThrowsWhenResizeShrinksPastOffsetDuringIteration()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(80, 160, realm);
        var typedArray = new JsBigInt64Array(rab, byteOffset: 16, length: 8, isLengthTracking: true);
        for (var i = 0; i < 8; i++)
        {
            typedArray.SetElement(i, new JsBigInt(i + 2));
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 8,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.True(threw);
        Assert.Equal([2d, 3d], values);
    }

    [Fact]
    public void BigIntLengthTrackingViewGrowsDuringIterationAndReadsZeroes()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(80, 160, realm);
        var typedArray = new JsBigInt64Array(rab, 0, length: 10, isLengthTracking: true);
        for (var i = 0; i < 10; i++)
        {
            typedArray.SetElement(i, new JsBigInt(i));
        }

        var values = CollectValuesWithResize(typedArray, rab, resizeAfter: 2, newByteLength: 160,
            out var resized, out var threw);

        Assert.True(resized);
        Assert.False(threw);
        Assert.Equal(20, values.Count);
        Assert.Equal([0d, 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 9d], values.Take(10).ToArray());
        Assert.Equal(new double[10], values.Skip(10).ToArray());
    }

    [Fact]
    public void StressTypedArrayIterationDoesNotStopEarly()
    {
        // Stress: managed iterator path (C# JsArrayIterator) to isolate TypedArray iteration without JS for-of.
        // Enable with JSENGINE_STRESS_TYPEDARRAY_ITERATION=1, control iterations via
        // JSENGINE_STRESS_TYPEDARRAY_ITERATION_COUNT (default 2000).
        if (!IsEnvEnabled("JSENGINE_STRESS_TYPEDARRAY_ITERATION"))
        {
            return;
        }

        var iterations = GetStressIterations("JSENGINE_STRESS_TYPEDARRAY_ITERATION_COUNT", 2000);
        for (var run = 0; run < iterations; run++)
        {
            var realm = new RealmState();
            var rab = new JsArrayBuffer(10, 20, realm);
            var fixedView = new JsUint8Array(rab, 0, 3);
            for (var i = 0; i < 3; i++)
            {
                fixedView.SetElement(i, i);
            }

            var fixedValues = CollectValuesWithResize(fixedView, rab, resizeAfter: int.MaxValue, newByteLength: 10,
                out var fixedResized, out var fixedThrew);
            Assert.False(fixedResized);
            Assert.False(fixedThrew);
            Assert.Equal([0d, 1d, 2d], fixedValues);

            var trackingView = new JsUint8Array(rab, 0, length: 10, isLengthTracking: true);
            for (var i = 0; i < 10; i++)
            {
                trackingView.SetElement(i, i);
            }

            var trackingValues = CollectValuesWithResize(trackingView, rab, resizeAfter: 2, newByteLength: 20,
                out var trackingResized, out var trackingThrew);
            Assert.True(trackingResized);
            Assert.False(trackingThrew);
            Assert.Equal(20, trackingValues.Count);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task StressTypedArrayIteratorNext_JsProtocol()
    {
        // Stress: JS iterator protocol path (manual iterator.next()) without for-of.
        // Enable with JSENGINE_STRESS_TYPEDARRAY_ITERATOR_NEXT_JS=1, control iterations via
        // JSENGINE_STRESS_TYPEDARRAY_ITERATOR_NEXT_JS_COUNT (default 200).
        if (!IsEnvEnabled("JSENGINE_STRESS_TYPEDARRAY_ITERATOR_NEXT_JS"))
        {
            return;
        }

        await using var engine = CreateEngine(() => new JsEngineOptions());
        var iterations = GetStressIterations("JSENGINE_STRESS_TYPEDARRAY_ITERATOR_NEXT_JS_COUNT", 200);

        await engine.Evaluate($$"""
                                 (function() {
                                     function assertArrayEq(actual, expected, label) {
                                         if (actual.length !== expected.length) {
                                             throw new Error(label + " length mismatch " + actual.length + " vs " + expected.length);
                                         }
                                         for (var i = 0; i < actual.length; i++) {
                                             if (actual[i] !== expected[i]) {
                                                 throw new Error(label + " mismatch at " + i + ": " + actual[i] + " vs " + expected[i]);
                                             }
                                         }
                                     }

                                     function expectThrow(fn, label) {
                                         var threw = false;
                                         try {
                                             fn();
                                         } catch (e) {
                                             threw = true;
                                         }
                                         if (!threw) {
                                             throw new Error(label + " expected throw");
                                         }
                                     }

                                     function collectWithIterator(iterable, rab, resizeAfter, newByteLength) {
                                         var it = iterable[Symbol.iterator]();
                                         var values = [];
                                         var resized = false;
                                         while (true) {
                                             var step = it.next();
                                             if (step.done) {
                                                 break;
                                             }
                                             values.push(Number(step.value));
                                             if (!resized && values.length === resizeAfter) {
                                                 rab.resize(newByteLength);
                                                 resized = true;
                                             }
                                         }
                                         if (!resized) {
                                             throw new Error("resize not hit");
                                         }
                                         return values;
                                     }

                                     function createRab(byteLength, maxByteLength) {
                                         return new ArrayBuffer(byteLength, { maxByteLength: maxByteLength });
                                     }

                                     function runOnce() {
                                         var ctors = [
                                             Uint8Array,
                                             Int8Array,
                                             Uint16Array,
                                             Int16Array,
                                             Uint32Array,
                                             Int32Array,
                                             Float32Array,
                                             Float64Array,
                                             Uint8ClampedArray
                                         ];
                                         if (typeof BigInt64Array !== "undefined") {
                                             ctors.push(BigInt64Array);
                                         }
                                         if (typeof BigUint64Array !== "undefined") {
                                             ctors.push(BigUint64Array);
                                         }

                                         for (var c = 0; c < ctors.length; c++) {
                                             var ctor = ctors[c];
                                             var elements = 10;
                                             var offset = 2;
                                             var byteLength = elements * ctor.BYTES_PER_ELEMENT;
                                             var byteOffset = offset * ctor.BYTES_PER_ELEMENT;

                                             var rab = createRab(byteLength, byteLength * 2);
                                             var fixed = new ctor(rab, 0, 3);
                                             for (var i = 0; i < 3; i++) {
                                                 fixed[i] = i;
                                             }
                                             expectThrow(function() {
                                                 collectWithIterator(fixed, rab, 2, 0);
                                             }, "fixed shrink to zero");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var fixedOffset = new ctor(rab, byteOffset, 3);
                                             for (var j = 0; j < 3; j++) {
                                                 fixedOffset[j] = j + offset;
                                             }
                                             var values = collectWithIterator(fixedOffset, rab, 2, byteLength / 2);
                                             assertArrayEq(values, [2, 3, 4], "fixed offset shrink in-bounds");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var tracking = new ctor(rab);
                                             for (var k = 0; k < elements; k++) {
                                                 tracking[k] = k;
                                             }
                                             values = collectWithIterator(tracking, rab, 2, byteLength * 2);
                                             if (values.length !== 20) {
                                                 throw new Error("tracking grow length mismatch " + values.length);
                                             }
                                         }
                                     }

                                     for (var run = 0; run < {{iterations}}; run++) {
                                         runOnce();
                                     }
                                 })();
                                 """);
    }

    [Fact(Timeout = 20000)]
    public async Task ForOfResizableTypedArray_JsSmoke()
    {
        // Stress: JS for-of path (full language iterator + binding + body evaluation).
        // Enable with JSENGINE_STRESS_FOROF_RAB=1 (default iterations=200, else 1).
        await using var engine = CreateEngine(() => new JsEngineOptions());
        var iterations = IsEnvEnabled("JSENGINE_STRESS_FOROF_RAB") ? 200 : 1;

        await engine.Evaluate($$"""
                                 (function() {
                                     function assertArrayEq(actual, expected, label) {
                                         if (actual.length !== expected.length) {
                                             throw new Error(label + " length mismatch " + actual.length + " vs " + expected.length);
                                         }
                                         for (var i = 0; i < actual.length; i++) {
                                             if (actual[i] !== expected[i]) {
                                                 throw new Error(label + " mismatch at " + i + ": " + actual[i] + " vs " + expected[i]);
                                             }
                                         }
                                     }

                                     function expectThrow(fn, label) {
                                         var threw = false;
                                         try {
                                             fn();
                                         } catch (e) {
                                             threw = true;
                                         }
                                         if (!threw) {
                                             throw new Error(label + " expected throw");
                                         }
                                     }

                                     function createRab(byteLength, maxByteLength) {
                                         return new ArrayBuffer(byteLength, { maxByteLength: maxByteLength });
                                     }

                                     function isBigIntArray(ta) {
                                         if (typeof BigInt64Array !== "undefined" && ta instanceof BigInt64Array) {
                                             return true;
                                         }
                                         if (typeof BigUint64Array !== "undefined" && ta instanceof BigUint64Array) {
                                             return true;
                                         }
                                         return false;
                                     }

                                     function fill(ta) {
                                         for (var i = 0; i < ta.length; i++) {
                                             var value = i % 128;
                                             ta[i] = isBigIntArray(ta) ? BigInt(value) : value;
                                         }
                                     }

                                     function collectAndResize(iterable, rab, resizeAfter, newByteLength) {
                                         var values = [];
                                         var resized = false;
                                         var arrayValues = false;
                                         for (var value of iterable) {
                                             if (Array.isArray(value)) {
                                                 arrayValues = true;
                                                 values.push([value[0], Number(value[1])]);
                                             } else {
                                                 values.push(Number(value));
                                             }
                                             if (!resized && values.length === resizeAfter) {
                                                 rab.resize(newByteLength);
                                                 resized = true;
                                             }
                                         }
                                         if (arrayValues) {
                                             throw new Error("unexpected array values");
                                         }
                                         return values;
                                     }

                                     function runOnce() {
                                         var ctors = [
                                             Uint8Array,
                                             Int8Array,
                                             Uint16Array,
                                             Int16Array,
                                             Uint32Array,
                                             Int32Array,
                                             Float32Array,
                                             Float64Array,
                                             Uint8ClampedArray
                                         ];
                                         if (typeof BigInt64Array !== "undefined") {
                                             ctors.push(BigInt64Array);
                                         }
                                         if (typeof BigUint64Array !== "undefined") {
                                             ctors.push(BigUint64Array);
                                         }

                                         for (var c = 0; c < ctors.length; c++) {
                                             var ctor = ctors[c];
                                             var elements = 10;
                                             var offset = 2;
                                             var byteLength = elements * ctor.BYTES_PER_ELEMENT;
                                             var byteOffset = offset * ctor.BYTES_PER_ELEMENT;

                                             var rab = createRab(byteLength, byteLength * 2);
                                             var fixed = new ctor(rab, 0, 3);
                                             fill(fixed);
                                             expectThrow(function() {
                                                 collectAndResize(fixed, rab, 2, 0);
                                             }, "fixed shrink to zero");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var tracking = new ctor(rab);
                                             fill(tracking);
                                             var values = collectAndResize(tracking, rab, 2, 0);
                                             assertArrayEq(values, [0, 1], "tracking shrink to zero");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var trackingOffset = new ctor(rab, byteOffset);
                                             fill(trackingOffset);
                                             expectThrow(function() {
                                                 collectAndResize(trackingOffset, rab, 2, 0);
                                             }, "tracking shrink past offset");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var grow = new ctor(rab);
                                             fill(grow);
                                             values = collectAndResize(grow, rab, 2, byteLength * 2);
                                             if (values.length !== 20) {
                                                 throw new Error("tracking grow length mismatch " + values.length);
                                             }
                                             for (var i = 0; i < 10; i++) {
                                                 if (values[i] !== i) {
                                                     throw new Error("tracking grow prefill mismatch at " + i);
                                                 }
                                             }
                                             for (var j = 10; j < 20; j++) {
                                                 if (values[j] !== 0) {
                                                     throw new Error("tracking grow zero-fill mismatch at " + j);
                                                 }
                                             }
                                         }
                                     }

                                     for (var run = 0; run < {{iterations}}; run++) {
                                         runOnce();
                                     }
                                 })();
                                 """);
    }

    [Fact(Timeout = 20000)]
    public async Task ForLoopResizableTypedArray_JsSmoke()
    {
        // Stress: JS indexed loop path (no iterator protocol).
        // Enable with JSENGINE_STRESS_FORLOOP_RAB=1 (default iterations=200, else 1).
        await using var engine = CreateEngine(() => new JsEngineOptions());
        var iterations = IsEnvEnabled("JSENGINE_STRESS_FORLOOP_RAB") ? 200 : 1;

        await engine.Evaluate($$"""
                                 (function() {
                                     function assertArrayEq(actual, expected, label) {
                                         if (actual.length !== expected.length) {
                                             throw new Error(label + " length mismatch " + actual.length + " vs " + expected.length);
                                         }
                                         for (var i = 0; i < actual.length; i++) {
                                             if (actual[i] !== expected[i]) {
                                                 throw new Error(label + " mismatch at " + i + ": " + actual[i] + " vs " + expected[i]);
                                             }
                                         }
                                     }

                                     function expectThrow(fn, label) {
                                         var threw = false;
                                         try {
                                             fn();
                                         } catch (e) {
                                             threw = true;
                                         }
                                         if (!threw) {
                                             throw new Error(label + " expected throw");
                                         }
                                     }

                                     function createRab(byteLength, maxByteLength) {
                                         return new ArrayBuffer(byteLength, { maxByteLength: maxByteLength });
                                     }

                                     function isBigIntArray(ta) {
                                         if (typeof BigInt64Array !== "undefined" && ta instanceof BigInt64Array) {
                                             return true;
                                         }
                                         if (typeof BigUint64Array !== "undefined" && ta instanceof BigUint64Array) {
                                             return true;
                                         }
                                         return false;
                                     }

                                     function fill(ta) {
                                         for (var i = 0; i < ta.length; i++) {
                                             var value = i % 128;
                                             ta[i] = isBigIntArray(ta) ? BigInt(value) : value;
                                         }
                                     }

                                     function fillBuffer(rab, ctor) {
                                         var writer = new ctor(rab);
                                         fill(writer);
                                     }

                                     function collectAndResizeIndex(iterable, rab, resizeAfter, newByteLength) {
                                         var values = [];
                                         var resized = false;
                                         for (var i = 0; i < iterable.length; i++) {
                                             values.push(Number(iterable[i]));
                                             if (!resized && values.length === resizeAfter) {
                                                 rab.resize(newByteLength);
                                                 resized = true;
                                             }
                                         }
                                         return values;
                                     }

                                     function runOnce() {
                                         var ctors = [
                                             Uint8Array,
                                             Int8Array,
                                             Uint16Array,
                                             Int16Array,
                                             Uint32Array,
                                             Int32Array,
                                             Float32Array,
                                             Float64Array,
                                             Uint8ClampedArray
                                         ];
                                         if (typeof BigInt64Array !== "undefined") {
                                             ctors.push(BigInt64Array);
                                         }
                                         if (typeof BigUint64Array !== "undefined") {
                                             ctors.push(BigUint64Array);
                                         }

                                         for (var c = 0; c < ctors.length; c++) {
                                             var ctor = ctors[c];
                                             var elements = 10;
                                             var offset = 2;
                                             var byteLength = elements * ctor.BYTES_PER_ELEMENT;
                                             var byteOffset = offset * ctor.BYTES_PER_ELEMENT;

                                             var rab = createRab(byteLength, byteLength * 2);
                                             var fixed = new ctor(rab, 0, 3);
                                             fillBuffer(rab, ctor);
                                             var fixedValues = collectAndResizeIndex(fixed, rab, 2, byteLength * 2);
                                             assertArrayEq(fixedValues, [0, 1, 2], "fixed grow");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var fixedOffset = new ctor(rab, byteOffset, 3);
                                             fillBuffer(rab, ctor);
                                             fixedValues = collectAndResizeIndex(fixedOffset, rab, 2, byteLength * 2);
                                             assertArrayEq(fixedValues, [2, 3, 4], "fixed offset grow");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var tracking = new ctor(rab);
                                             fillBuffer(rab, ctor);
                                             var values = collectAndResizeIndex(tracking, rab, 2, byteLength * 2);
                                             if (values.length !== 20) {
                                                 throw new Error("tracking grow length mismatch " + values.length);
                                             }
                                             for (var i = 0; i < 10; i++) {
                                                 if (values[i] !== i) {
                                                     throw new Error("tracking grow prefill mismatch at " + i);
                                                 }
                                             }
                                             for (var j = 10; j < 20; j++) {
                                                 if (values[j] !== 0) {
                                                     throw new Error("tracking grow zero-fill mismatch at " + j);
                                                 }
                                             }

                                             rab = createRab(byteLength, byteLength * 2);
                                             var trackingOffset = new ctor(rab, byteOffset);
                                             fillBuffer(rab, ctor);
                                             values = collectAndResizeIndex(trackingOffset, rab, 2, byteLength * 2);
                                             if (values.length !== 20 - offset) {
                                                 throw new Error("tracking offset grow length mismatch " + values.length);
                                             }

                                             rab = createRab(byteLength, byteLength * 2);
                                             var fixedShrink = new ctor(rab, 0, 3);
                                             fillBuffer(rab, ctor);
                                             values = collectAndResizeIndex(fixedShrink, rab, 2, 0);
                                             assertArrayEq(values, [0, 1], "fixed shrink to zero");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var trackingShrink = new ctor(rab);
                                             fillBuffer(rab, ctor);
                                             values = collectAndResizeIndex(trackingShrink, rab, 2, 0);
                                             assertArrayEq(values, [0, 1], "tracking shrink to zero");
                                         }
                                     }

                                     for (var run = 0; run < {{iterations}}; run++) {
                                         runOnce();
                                     }
                                 })();
                                 """);
    }

    [Fact(Timeout = 20000)]
    public async Task ForOfResizableTypedArray_JsSmoke_Parity()
    {
        // Stress: JS for-of path using the same helper structure as ForLoopResizableTypedArray_JsSmoke.
        // Enable with JSENGINE_STRESS_FOROF_RAB_PARITY=1 (default iterations=200, else 1).
        await using var engine = CreateEngine(() => new JsEngineOptions());
        var iterations = IsEnvEnabled("JSENGINE_STRESS_FOROF_RAB_PARITY") ? 200 : 1;

        await engine.Evaluate($$"""
                                 (function() {
                                     function assertArrayEq(actual, expected, label) {
                                         if (actual.length !== expected.length) {
                                             throw new Error(label + " length mismatch " + actual.length + " vs " + expected.length);
                                         }
                                         for (var i = 0; i < actual.length; i++) {
                                             if (actual[i] !== expected[i]) {
                                                 throw new Error(label + " mismatch at " + i + ": " + actual[i] + " vs " + expected[i]);
                                             }
                                         }
                                     }

                                     function expectThrow(fn, label) {
                                         var threw = false;
                                         try {
                                             fn();
                                         } catch (e) {
                                             threw = true;
                                         }
                                         if (!threw) {
                                             throw new Error(label + " expected throw");
                                         }
                                     }

                                     function createRab(byteLength, maxByteLength) {
                                         return new ArrayBuffer(byteLength, { maxByteLength: maxByteLength });
                                     }

                                     function isBigIntArray(ta) {
                                         if (typeof BigInt64Array !== "undefined" && ta instanceof BigInt64Array) {
                                             return true;
                                         }
                                         if (typeof BigUint64Array !== "undefined" && ta instanceof BigUint64Array) {
                                             return true;
                                         }
                                         return false;
                                     }

                                     function fill(ta) {
                                         for (var i = 0; i < ta.length; i++) {
                                             var value = i % 128;
                                             ta[i] = isBigIntArray(ta) ? BigInt(value) : value;
                                         }
                                     }

                                     function fillBuffer(rab, ctor) {
                                         var writer = new ctor(rab);
                                         fill(writer);
                                     }

                                     function collectAndResizeForOf(iterable, rab, resizeAfter, newByteLength) {
                                         var values = [];
                                         var resized = false;
                                         for (var value of iterable) {
                                             values.push(Number(value));
                                             if (!resized && values.length === resizeAfter) {
                                                 rab.resize(newByteLength);
                                                 resized = true;
                                             }
                                         }
                                         return values;
                                     }

                                     function runOnce() {
                                         var ctors = [
                                             Uint8Array,
                                             Int8Array,
                                             Uint16Array,
                                             Int16Array,
                                             Uint32Array,
                                             Int32Array,
                                             Float32Array,
                                             Float64Array,
                                             Uint8ClampedArray
                                         ];
                                         if (typeof BigInt64Array !== "undefined") {
                                             ctors.push(BigInt64Array);
                                         }
                                         if (typeof BigUint64Array !== "undefined") {
                                             ctors.push(BigUint64Array);
                                         }

                                         for (var c = 0; c < ctors.length; c++) {
                                             var ctor = ctors[c];
                                             var elements = 10;
                                             var offset = 2;
                                             var byteLength = elements * ctor.BYTES_PER_ELEMENT;
                                             var byteOffset = offset * ctor.BYTES_PER_ELEMENT;

                                             var rab = createRab(byteLength, byteLength * 2);
                                             var fixed = new ctor(rab, 0, 3);
                                             fillBuffer(rab, ctor);
                                             var fixedValues = collectAndResizeForOf(fixed, rab, 2, byteLength * 2);
                                             assertArrayEq(fixedValues, [0, 1, 2], "fixed grow");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var fixedOffset = new ctor(rab, byteOffset, 3);
                                             fillBuffer(rab, ctor);
                                             fixedValues = collectAndResizeForOf(fixedOffset, rab, 2, byteLength * 2);
                                             assertArrayEq(fixedValues, [2, 3, 4], "fixed offset grow");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var tracking = new ctor(rab);
                                             fillBuffer(rab, ctor);
                                             var values = collectAndResizeForOf(tracking, rab, 2, byteLength * 2);
                                             if (values.length !== 20) {
                                                 throw new Error("tracking grow length mismatch " + values.length);
                                             }
                                             for (var i = 0; i < 10; i++) {
                                                 if (values[i] !== i) {
                                                     throw new Error("tracking grow prefill mismatch at " + i);
                                                 }
                                             }
                                             for (var j = 10; j < 20; j++) {
                                                 if (values[j] !== 0) {
                                                     throw new Error("tracking grow zero-fill mismatch at " + j);
                                                 }
                                             }

                                             rab = createRab(byteLength, byteLength * 2);
                                             var trackingOffset = new ctor(rab, byteOffset);
                                             fillBuffer(rab, ctor);
                                             values = collectAndResizeForOf(trackingOffset, rab, 2, byteLength * 2);
                                             if (values.length !== 20 - offset) {
                                                 throw new Error("tracking offset grow length mismatch " + values.length);
                                             }

                                             rab = createRab(byteLength, byteLength * 2);
                                             var fixedShrink = new ctor(rab, 0, 3);
                                             fillBuffer(rab, ctor);
                                             expectThrow(function() {
                                                 collectAndResizeForOf(fixedShrink, rab, 2, 0);
                                             }, "fixed shrink to zero");

                                             rab = createRab(byteLength, byteLength * 2);
                                             var trackingShrink = new ctor(rab);
                                             fillBuffer(rab, ctor);
                                             values = collectAndResizeForOf(trackingShrink, rab, 2, 0);
                                             assertArrayEq(values, [0, 1], "tracking shrink to zero");
                                         }
                                     }

                                     for (var run = 0; run < {{iterations}}; run++) {
                                         runOnce();
                                     }
                                 })();
                                 """);
    }

    private static List<double> CollectValuesWithResize(
        TypedArrayBase typedArray,
        JsArrayBuffer buffer,
        int resizeAfter,
        int newByteLength,
        out bool resized,
        out bool threw)
    {
        var realm = buffer.RealmState ?? new RealmState();
        var iterator = new JsArrayIterator(typedArray, ArrayIteratorKind.Values, realm, prototype: null);
        var values = new List<double>();
        resized = false;
        threw = false;

        while (true)
        {
            JsValue step;
            try
            {
                step = iterator.Next();
            }
            catch (ThrowSignal)
            {
                threw = true;
                break;
            }

            if (!step.TryGetObject<IJsPropertyAccessor>(out var stepObj))
            {
                throw new InvalidOperationException("Iterator result is not an object.");
            }

            var done = stepObj.TryGetProperty("done", out var doneValue) &&
                       JsOps.ToBoolean(doneValue);
            var hasValue = stepObj.TryGetProperty("value", out var value);

            if (stepObj is IteratorResultObject poolable)
            {
                IteratorResultObjectPool.Return(poolable);
            }

            if (done)
            {
                break;
            }

            values.Add(hasValue ? ConvertToDouble(value, realm) : double.NaN);

            if (!resized && values.Count == resizeAfter)
            {
                buffer.Resize(newByteLength);
                resized = true;
            }
        }

        return values;
    }

    private static double ConvertToDouble(JsValue value, RealmState realm)
    {
        if (value.IsBigInt && value.ObjectValue is JsBigInt bigInt)
        {
            return (double)bigInt.Value;
        }

        return JsOps.ToNumber(value, realm.CreateContext());
    }

    private static int GetStressIterations(string name, int defaultValue)
    {
        var setting = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(setting))
        {
            return defaultValue;
        }

        return int.TryParse(setting, out var parsed) && parsed > 0 ? parsed : defaultValue;
    }

    private static bool IsEnvEnabled(string name)
    {
        var setting = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(setting))
        {
            return false;
        }

        return !string.Equals(setting, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(setting, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase);
    }
}

namespace Asynkron.JsEngine.Tests;

public class AdditionalArrayMethodsTests
{
    [Fact(Timeout = 2000)]
    public async Task Array_Fill_FillsWithValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.fill(0);
                                                       arr[2];

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Fill_WithStartAndEnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.fill(0, 2, 4);
                                                       arr[0] + arr[2] + arr[4];

                                           """);
        Assert.Equal(1d + 0d + 5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Fill_WithNegativeIndices()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.fill(0, -3, -1);
                                                       arr[2] + arr[3];

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Fill_DefaultsToUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const arr = [1, 2];
                                                       arr.fill();
                                                       return arr[0] === undefined && arr[1] === undefined;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_CopyWithin_CopiesElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.copyWithin(0, 3);
                                                       arr[0] + arr[1];

                                           """);
        Assert.Equal(4d + 5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_CopyWithin_WithAllArguments()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       arr.copyWithin(1, 3, 4);
                                                       arr[1];

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_CopyWithin_CoercesWhenArgumentsMissing()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let invoked = false;
                                                       const target = {
                                                         get length() {
                                                           invoked = true;
                                                           return 2;
                                                         },
                                                         0: "a",
                                                         1: "b"
                                                       };
                                                       Array.prototype.copyWithin.call(target);
                                                       return invoked && target[0] === "a" && target[1] === "b";

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_CopyWithin_ThrowsWhenDeleteFails()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const target = {0: "a", 1: "b", length: 2};
                                                       const proxy = new Proxy(target, {
                                                         has(t, key) {
                                                           if (key === "1") {
                                                             return false;
                                                           }
                                                           return Reflect.has(t, key);
                                                         },
                                                         deleteProperty(t, key) {
                                                           if (key === "0") {
                                                             return false;
                                                           }
                                                           return Reflect.deleteProperty(t, key);
                                                         }
                                                       });
                                                       try {
                                                         Array.prototype.copyWithin.call(proxy, 0, 1);
                                                         return false;
                                                       } catch (err) {
                                                         return err instanceof TypeError;
                                                       }

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToSorted_ReturnsSortedCopy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [3, 1, 4, 1, 5];
                                                       let sorted = arr.toSorted(function(a, b) { return a - b; });
                                                       arr[0] + sorted[0];

                                           """);
        Assert.Equal(3d + 1d, result); // original unchanged, sorted is [1,1,3,4,5]
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToReversed_ReturnsReversedCopy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let reversed = arr.toReversed();
                                                       arr[0] + reversed[0];

                                           """);
        Assert.Equal(1d + 5d, result); // original unchanged
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToSpliced_ReturnsModifiedCopy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let spliced = arr.toSpliced(2, 2, 99);
                                                       arr.length + spliced.length + spliced[2];

                                           """);
        Assert.Equal(5d + 4d + 99d, result); // original unchanged, spliced is [1,2,99,5]
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToSpliced_TreatsUndefinedDeleteCountAsWholeTail()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const arr = [1, 2, 3, 4];
                                                       const spliced = arr.toSpliced(1, undefined, 9);
                                                       return spliced.length === 2 && spliced[0] === 1 && spliced[1] === 9;

                                           """);
        Assert.Equal(true, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Array_With_ReplacesElement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let modified = arr.with(2, 99);
                                                       arr[2] + modified[2];

                                           """);
        Assert.Equal(3d + 99d, result); // original unchanged
    }

    [Fact(Timeout = 2000)]
    public async Task Array_With_HandlesNegativeIndex()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3, 4, 5];
                                                       let modified = arr.with(-1, 99);
                                                       modified[4];

                                           """);
        Assert.Equal(99d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_UsesIteratorAndMapper()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const iterable = {
                                                         [Symbol.iterator]() {
                                                           let i = 0;
                                                           return {
                                                             next() {
                                                               if (i < 2) {
                                                                 const value = i + 1;
                                                                 i++;
                                                                 return { value, done: false };
                                                               }
                                                               return { done: true };
                                                             }
                                                           };
                                                         }
                                                       };
                                                       const receiver = { score: 0 };
                                                       const output = Array.from(iterable, function(value, index) {
                                                         this.score += value + index;
                                                         return value * 2;
                                                       }, receiver);
                                                       output[0] * 100 + output[1] * 10 + receiver.score;

                                           """);
        Assert.Equal(244d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_RespectsConstructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       class CustomArray extends Array {
                                                         constructor(...args) {
                                                           super(...args);
                                                           this.created = true;
                                                         }
                                                       }
                                                       const arr = Array.from.call(CustomArray, { length: 2, 0: "a", 1: "b" });
                                                       arr instanceof CustomArray && arr.created && arr[1] === "b";

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_ClosesIteratorOnMapperError()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let closed = 0;
                                                       const iterable = {
                                                         [Symbol.iterator]() {
                                                           return {
                                                             next() { return { value: 1, done: false }; },
                                                             return() { closed++; return { done: true }; }
                                                           };
                                                         }
                                                       };
                                                       try {
                                                         Array.from(iterable, () => { throw new Error("boom"); });
                                                       } catch (err) {}
                                                       closed;

                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToString_UsesJoinWhenCallable()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const obj = {
                                                         join() { return "joined"; }
                                                       };
                                                       Array.prototype.toString.call(obj);

                                           """);
        Assert.Equal("joined", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToString_FallsBackToObjectPrototype()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const obj = { foo: 1 };
                                                       delete obj.join;
                                                       Array.prototype.toString.call(obj);

                                           """);
        Assert.Equal("[object Object]", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Map_DoesNotInvokeGetForMissingProxyIndices()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let getCount = 0;
                                                       const proxy = new Proxy({ length: 1 }, {
                                                         has(target, prop) {
                                                           if (prop === "0") {
                                                             return false;
                                                           }
                                                           return Reflect.has(target, prop);
                                                         },
                                                         get(target, prop, receiver) {
                                                           if (prop === "0") {
                                                             getCount++;
                                                             throw new Error("get should not run");
                                                           }
                                                           return Reflect.get(target, prop, receiver);
                                                         }
                                                       });
                                                       Array.prototype.map.call(proxy, () => 1);
                                                       getCount;

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ForEach_QueriesHasBeforeGet()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let hasCount = 0;
                                                       let getCount = 0;
                                                       const proxy = new Proxy({ 0: "x", length: 1 }, {
                                                         has(target, prop) {
                                                           if (prop === "0") {
                                                             hasCount++;
                                                           }
                                                           return Reflect.has(target, prop);
                                                         },
                                                         get(target, prop, receiver) {
                                                           if (prop === "0") {
                                                             getCount++;
                                                           }
                                                           return Reflect.get(target, prop, receiver);
                                                         }
                                                       });
                                                       Array.prototype.forEach.call(proxy, () => {});
                                                       hasCount * 10 + getCount;

                                           """);
        Assert.Equal(11d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Join_UsesPrototypeValues()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       const base = {
                                                         get 0() { return "proto"; }
                                                       };
                                                           const obj = Object.create(base);
                                                           obj.length = 1;
                                                           Array.prototype.join.call(obj);

                                           """);
        Assert.Equal("proto", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Join_InvokesGetWhenElementExists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let getCount = 0;
                                                       const proxy = new Proxy({ 0: "value", length: 1 }, {
                                                         get(target, prop, receiver) {
                                                           if (prop === "0") {
                                                             getCount++;
                                                           }
                                                           return Reflect.get(target, prop, receiver);
                                                         }
                                                       });
                                                       Array.prototype.join.call(proxy);
                                                       getCount;

                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Slice_SkipsMissingProxyIndex()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let getCount = 0;
                                                       const proxy = new Proxy({ length: 1 }, {
                                                         has(target, prop) {
                                                           if (prop === "0") {
                                                             return false;
                                                           }
                                                           return Reflect.has(target, prop);
                                                         },
                                                         get(target, prop, receiver) {
                                                           if (prop === "0") {
                                                             getCount++;
                                                           }
                                                           return Reflect.get(target, prop, receiver);
                                                         }
                                                       });
                                                       Array.prototype.slice.call(proxy, 0, 1);
                                                       getCount;

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Slice_ReadsExistingProxyIndex()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let getCount = 0;
                                                       const proxy = new Proxy({ 0: "value", length: 1 }, {
                                                         get(target, prop, receiver) {
                                                           if (prop === "0") {
                                                             getCount++;
                                                           }
                                                           return Reflect.get(target, prop, receiver);
                                                         }
                                                       });
                                                       Array.prototype.slice.call(proxy, 0, 1)[0];
                                                       getCount;

                                           """);
        Assert.Equal(1d, result);
    }
}

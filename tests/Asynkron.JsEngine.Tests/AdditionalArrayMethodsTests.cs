using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class AdditionalArrayMethodsTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Array_Fill_FillsWithValue()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const arr = [1, 2, 3, 4];
                                                       const spliced = arr.toSpliced(1, undefined, 9);
                                                       return spliced.length === 2 && spliced[0] === 1 && spliced[1] === 9;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ReflectConstruct_UsesNewTargetRealmPrototype()
    {
        await using var engine = CreateEngine();
        await using var otherRealm = CreateEngine();

        var foreignCtor = await otherRealm.Evaluate("""
                                                       (function() {
                                                         function Foreign() {}
                                                         return Foreign;
                                                       })();
                                           """);
        var foreignArrayPrototype = await otherRealm.Evaluate("Array.prototype");
        engine.SetGlobalValue("foreignCtor", foreignCtor);
        engine.SetGlobalValue("foreignArrayPrototype", foreignArrayPrototype);

        var result = await engine.Evaluate("""
                                                       foreignCtor.prototype = undefined;
                                                       const arr = Reflect.construct(Array, [1], foreignCtor);
                                                       Object.getPrototypeOf(arr) === foreignArrayPrototype;
                                           """);
        Assert.Equal(true, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Array_With_ReplacesElement()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
    public async Task Array_From_StopsWhenSourceShrinks()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let source = [0, 1, -2, 4, -8, 16];
                                                       let seen = [];
                                                       const output = Array.from(source, function(value, index) {
                                                         seen.push({ value, index });
                                                         source.pop();
                                                         return 127;
                                                       });
                                                       return {
                                                         outLength: output.length,
                                                         seenCount: seen.length,
                                                         entries: output
                                                       };

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(3d, obj["outLength"]);
        Assert.Equal(3d, obj["seenCount"]);
        var entries = Assert.IsType<JsArray>(obj["entries"]);
        Assert.Equal(127d, entries.GetElement(0).ToObject());
        Assert.Equal(127d, entries.GetElement(1).ToObject());
        Assert.Equal(127d, entries.GetElement(2).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_RespectsConstructor()
    {
        await using var engine = CreateEngine();
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
    public async Task Array_From_IteratorClosesWhenElementCannotBeDefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let closeCount = 0;
                                                       const items = {
                                                         [Symbol.iterator]() {
                                                           let called = false;
                                                           return {
                                                             next() {
                                                               if (called) {
                                                                 return { done: true };
                                                               }
                                                               called = true;
                                                               return { value: "x", done: false };
                                                             },
                                                             return() { closeCount++; return { done: true }; }
                                                           };
                                                         }
                                                       };
                                                       function Trap() {
                                                         Object.defineProperty(this, "0", {
                                                           writable: true,
                                                           configurable: false
                                                         });
                                                       }
                                                       try {
                                                         Array.from.call(Trap, items);
                                                         return { threw: "no throw", closeCount };
                                                       } catch (err) {
                                                         return {
                                                           threw: err instanceof TypeError,
                                                           closeCount
                                                         };
                                                       }

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["threw"]);
        Assert.Equal(1d, obj["closeCount"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_ArrayLikeThrowsWhenElementsCannotBeDefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const items = { length: 1 };
                                                       const ctors = [
                                                         function() {
                                                           this.length = 0;
                                                           Object.preventExtensions(this);
                                                         },
                                                         function() {
                                                           Object.defineProperty(this, "0", {
                                                             writable: true,
                                                             configurable: false
                                                           });
                                                         }
                                                       ];
                                                       return ctors.every(ctor => {
                                                         try {
                                                           Array.from.call(ctor, items);
                                                           return false;
                                                         } catch (err) {
                                                           return err instanceof TypeError;
                                                         }
                                                       });

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_FromAsync_ExposesCorrectMetadata()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       return {
                                                         type: typeof Array.fromAsync,
                                                         extensible: Object.isExtensible(Array.fromAsync),
                                                         prototypeIsFunction: Object.getPrototypeOf(Array.fromAsync) === Function.prototype,
                                                         hasOwnPrototype: Object.prototype.hasOwnProperty.call(Array.fromAsync, "prototype"),
                                                         lengthDesc: Object.getOwnPropertyDescriptor(Array.fromAsync, "length"),
                                                         nameDesc: Object.getOwnPropertyDescriptor(Array.fromAsync, "name"),
                                                         propDesc: Object.getOwnPropertyDescriptor(Array, "fromAsync")
                                                       };

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal("function", obj["type"]);
        Assert.Equal(true, obj["extensible"]);
        Assert.Equal(true, obj["prototypeIsFunction"]);
        Assert.Equal(false, obj["hasOwnPrototype"]);

        var lengthDesc = Assert.IsType<JsObject>(obj["lengthDesc"]);
        Assert.Equal(1d, lengthDesc["value"]);
        Assert.Equal(false, lengthDesc["writable"]);
        Assert.Equal(false, lengthDesc["enumerable"]);
        Assert.Equal(true, lengthDesc["configurable"]);

        var nameDesc = Assert.IsType<JsObject>(obj["nameDesc"]);
        Assert.Equal("fromAsync", nameDesc["value"]);
        Assert.Equal(false, nameDesc["writable"]);
        Assert.Equal(false, nameDesc["enumerable"]);
        Assert.Equal(true, nameDesc["configurable"]);

        var propDesc = Assert.IsType<JsObject>(obj["propDesc"]);
        Assert.Equal(true, propDesc["writable"]);
        Assert.Equal(false, propDesc["enumerable"]);
        Assert.Equal(true, propDesc["configurable"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_FromAsync_ConsumesAsyncIterables()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                                     async function* source() {
                                                       yield Promise.resolve(3);
                                                       yield 7;
                                                     }
                                                     Array.fromAsync(source(), (value, index) => value * index)
                                                       .then(result => { globalThis.__asyncResult = result; },
                                                             err => { globalThis.__asyncError = err; });

                                         """);

        Assert.False(engine.GlobalObject.ContainsKey("__asyncError"));
        Assert.True(engine.GlobalObject.TryGetValue("__asyncResult", out var stored));
        var arr = Assert.IsType<JsArray>(stored);
        Assert.Equal(0d, arr.GetElement(0).ToObject());
        Assert.Equal(7d, arr.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task Array_FromAsync_AwaitsArrayLikeValues()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                                     const source = {
                                                       0: Promise.resolve("a"),
                                                       1: "b",
                                                       get length() { return 2; }
                                                     };
                                                     Array.fromAsync(source, value => value + "!")
                                                       .then(result => { globalThis.__asyncArrayLike = result; },
                                                             err => { globalThis.__asyncArrayLikeError = err; });

                                         """);

        Assert.False(engine.GlobalObject.ContainsKey("__asyncArrayLikeError"));
        Assert.True(engine.GlobalObject.TryGetValue("__asyncArrayLike", out var stored));
        var arr = Assert.IsType<JsArray>(stored);
        Assert.Equal("a!", arr.GetElement(0).ToObject());
        Assert.Equal("b!", arr.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task Array_FromAsync_RejectsWhenArrayLikeLengthIsTooLarge()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                                     Array.fromAsync.call({}, { length: 4294967296 })
                                                       .then(
                                                         () => { globalThis.__asyncRangeStatus = "resolved"; },
                                                         err => { globalThis.__asyncRangeStatus = err && err.name; });

                                         """);
        Assert.True(engine.GlobalObject.TryGetValue("__asyncRangeStatus", out var status));
        Assert.Equal("RangeError", status);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_FromAsync_ClosesIteratorWhenCustomConstructorFailsToDefineElement()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                                     globalThis.__asyncCtorStatus = "pending";
                                                     globalThis.__asyncCtorClosed = 0;
                                                     function TrapArray() {
                                                       Object.defineProperty(this, 0, {
                                                         enumerable: true,
                                                         writable: true,
                                                         configurable: false,
                                                         value: 0
                                                       });
                                                     }
                                                     const iterator = {
                                                       next() { return { value: 1, done: false }; },
                                                       return() { globalThis.__asyncCtorClosed++; return { done: true }; },
                                                       [Symbol.iterator]() { return this; }
                                                     };
                                                     Array.fromAsync.call(TrapArray, iterator)
                                                       .then(
                                                         () => { globalThis.__asyncCtorStatus = "resolved"; },
                                                         err => { globalThis.__asyncCtorStatus = err && err.name; });

                                         """);
        Assert.Equal("TypeError", engine.GlobalObject["__asyncCtorStatus"]);
        Assert.Equal(1d, engine.GlobalObject["__asyncCtorClosed"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Concat_ThrowsWhenResultWouldExceedMaxLength()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                     var spreadable = { length: Number.MAX_SAFE_INTEGER };
                                                     spreadable[Symbol.isConcatSpreadable] = true;
                                                     try {
                                                       [1].concat(spreadable);
                                                       return "no throw";
                                                     } catch (err) {
                                                       return err instanceof TypeError;
                                                     }

                                         """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Concat_RespectsSymbolIsConcatSpreadable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                     const ta = new Uint8Array([5, 10]);
                                                     const defaultConcat = [].concat(ta);
                                                     const defaultBehavior = defaultConcat.length === 1 &&
                                                       defaultConcat[0] === ta;
                                                     ta[Symbol.isConcatSpreadable] = true;
                                                     const flattened = [].concat(ta);
                                                     const flattenedBehavior = flattened.length === 2 &&
                                                       flattened[0] === 5 &&
                                                       flattened[1] === 10;
                                                     return defaultBehavior && flattenedBehavior;

                                         """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Concat_EvaluatesSpreadableElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                     var observed = false;
                                                     var spreadable = {
                                                       length: 1,
                                                       get 0() {
                                                         observed = true;
                                                         throw new Error("poison");
                                                       }
                                                     };
                                                     spreadable[Symbol.isConcatSpreadable] = true;
                                                     try {
                                                       [].concat(spreadable);
                                                       return "no throw";
                                                     } catch (err) {
                                                       return observed && err.message === "poison";
                                                     }

                                         """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_MapFunctionMustBeCallable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const inputs = [null, {}, "string", true, 42];
                                                       return inputs.every(value => {
                                                         try {
                                                           Array.from([], value);
                                                           return false;
                                                         } catch (err) {
                                                           return err instanceof TypeError;
                                                         }
                                                       });

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_ConstructorsRunBeforeIterator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let iteratorCalled = false;
                                                       const items = {
                                                         get [Symbol.iterator]() {
                                                           return function() {
                                                             iteratorCalled = true;
                                                             return {
                                                               next() { return { done: true }; }
                                                             };
                                                           };
                                                         }
                                                       };
                                                       function CustomArray() {
                                                         throw new Error("ctor");
                                                       }
                                                       try {
                                                         Array.from.call(CustomArray, items);
                                                         return { threw: false, iteratorCalled };
                                                       } catch (err) {
                                                         return {
                                                           threw: err.message,
                                                           iteratorCalled
                                                         };
                                                       }

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal("ctor", obj["threw"]);
        Assert.Equal(false, obj["iteratorCalled"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_IteratorCloseIgnoresNonObjectReturnValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let closed = 0;
                                                       const iterable = {
                                                         [Symbol.iterator]() {
                                                           return {
                                                             next() { return { value: 1, done: false }; },
                                                             return() { closed++; return 0; }
                                                           };
                                                         }
                                                       };
                                                       try {
                                                         Array.from(iterable, () => { throw new Error("fail"); });
                                                       } catch (err) {
                                                         return { error: err.message, closed };
                                                       }

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal("fail", obj["error"]);
        Assert.Equal(1d, obj["closed"]);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictGlobalVar_BindsToGlobalObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       "use strict";
                                                       var marker = 42;
                                                       ({
                                                         thisIsGlobal: this === globalThis,
                                                         globalMarker: globalThis.marker,
                                                         thisMarker: this.marker
                                                       });

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["thisIsGlobal"]);
        Assert.Equal(42d, obj["globalMarker"]);
        Assert.Equal(42d, obj["thisMarker"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_From_ClosesIteratorOnMapperError()
    {
        await using var engine = CreateEngine();
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
    public async Task Array_Prototype_BehavesAsExoticArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const initialLength = Array.prototype.length;
                                                       const hadOwn = Object.prototype.hasOwnProperty.call(Array.prototype, "2");
                                                       const previous = Array.prototype[2];
                                                       Array.prototype[2] = 42;
                                                       const summary = {
                                                         length: Array.prototype.length,
                                                         zero: Array.prototype[0],
                                                         one: Array.prototype[1],
                                                         two: Array.prototype[2],
                                                         tag: Object.prototype.toString.call(Array.prototype)
                                                       };
                                                       if (hadOwn) {
                                                         Array.prototype[2] = previous;
                                                       } else {
                                                         delete Array.prototype[2];
                                                       }
                                                       Array.prototype.length = initialLength;
                                                       return summary;

                                           """);
        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(3d, obj["length"]);
        Assert.Same(Symbol.Undefined, obj["zero"]);
        Assert.Same(Symbol.Undefined, obj["one"]);
        Assert.Equal(42d, obj["two"]);
        Assert.Equal("[object Array]", obj["tag"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_ToString_UsesJoinWhenCallable()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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

public class FastPathAdditionalArrayMethodsTests(ITestOutputHelper output) : AdditionalArrayMethodsTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceAdditionalArrayMethodsTests(ITestOutputHelper output) : AdditionalArrayMethodsTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}

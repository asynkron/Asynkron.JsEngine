using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class DestructuringIteratorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ArrayPatternIteratorThrowsOriginalError()
    {
        await using var engine = CreateEngine();
        // This mirrors Test262's ary-init-iter-get-err case: the iterator getter throws.
        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            var iter = {};
            iter[Symbol.iterator] = function() {
              throw new Error("boom");
            };
        var f = ([x]) => {};
        f(iter);
        """));

        string? thrownName = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("name", out var name))
        {
            thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(name.ToObject()), null);
        }
        Assert.Equal("Error", thrownName);
    }

    [Fact]
    public async Task StrictArrayPatternIteratorThrowsOriginalError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            "use strict";
            var iter = {};
            iter[Symbol.iterator] = function() {
              throw new Error("boom-strict");
            };

            var f = ([x]) => {};
            f(iter);
            """));

        string? thrownName = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("name", out var name))
        {
            thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(name.ToObject()), null);
        }
        Assert.Equal("Error", thrownName);
    }

    [Fact]
    public async Task DeletedArrayIteratorThrowsTypeError()
    {
        // This mimics Test262's ary-init-iter-get-err-array-prototype.js test
        await using var engine = CreateEngine();

        // First, check what happens at each step
        Output.WriteLine("Step 1: Check Array before delete");
        try
        {
            var arrayCheck1 = await engine.Evaluate("typeof Array");
            Output.WriteLine($"  typeof Array: {arrayCheck1}");
        }
        catch (Exception e)
        {
            Output.WriteLine($"  Error checking Array: {e.Message}");
        }

        Output.WriteLine("Step 2: Execute delete");
        try
        {
            var deleteResult = await engine.Evaluate("delete Array.prototype[Symbol.iterator]");
            Output.WriteLine($"  delete result: {deleteResult}");
        }
        catch (Exception e)
        {
            Output.WriteLine($"  Error during delete: {e.Message}");
        }

        Output.WriteLine("Step 3: Check Array after delete");
        try
        {
            var arrayCheck2 = await engine.Evaluate("typeof Array");
            Output.WriteLine($"  typeof Array: {arrayCheck2}");
        }
        catch (Exception e)
        {
            Output.WriteLine($"  Error checking Array: {e.Message}");
        }

        Output.WriteLine("Step 4: Check Array.prototype");
        try
        {
            var protoCheck = await engine.Evaluate("typeof Array.prototype");
            Output.WriteLine($"  typeof Array.prototype: {protoCheck}");
        }
        catch (Exception e)
        {
            Output.WriteLine($"  Error checking Array.prototype: {e.Message}");
        }

        Output.WriteLine("Step 5: Try to create array literal");
        try
        {
            var arrLiteral = await engine.Evaluate("[1, 2, 3]");
            Output.WriteLine($"  Array literal result: {arrLiteral?.GetType().Name}");
        }
        catch (Exception e)
        {
            Output.WriteLine($"  Error creating array literal: {e.Message}");
        }

        Output.WriteLine("Step 6: Try destructuring (standalone)");
        try
        {
            await engine.Evaluate("var [x, y, z] = [1, 2, 3];");
            Output.WriteLine("  No error thrown!");
        }
        catch (ThrowSignal ts)
        {
            if (ts.ThrownValue.TryGetObject<JsObject>(out var errObj))
            {
                errObj.TryGetProperty("name", out var nameVal);
                errObj.TryGetProperty("message", out var msgVal);
                Output.WriteLine($"  Error: {nameVal} - {msgVal}");
            }
        }

        Output.WriteLine("Step 7: Try simplest failing case");
        await using var engine2 = CreateEngine();
        // Just check if Array is accessible in a fresh script
        try
        {
            var result = await engine2.Evaluate("typeof Array");
            Output.WriteLine($"  Simplest case - typeof Array: {result}");
        }
        catch (Exception e)
        {
            Output.WriteLine($"  Simplest case failed: {e.Message}");
        }

        // Now try with destructuring pattern
        Output.WriteLine("Step 8: Script with destructuring pattern");
        await using var engine3 = CreateEngine();
        var logger = new TestLogger();
        engine3.RealmState.Logger = logger;

        Output.WriteLine("  Engine3 GlobalEnvironment info:");
        var globalEnv = engine3.GlobalEnvironment;
        Output.WriteLine($"    GlobalEnvironment.HasSlots: {globalEnv.HasSlots}");
        Output.WriteLine($"    GlobalEnvironment.IsGlobalFunctionScope: {globalEnv.IsGlobalFunctionScope}");

        // Check if Symbol.This exists
        var hasSymbolThis = globalEnv.TryGetJsValue(Symbol.This, out var symbolThisVal);
        Output.WriteLine($"    GlobalEnvironment has Symbol.This: {hasSymbolThis}");
        if (hasSymbolThis)
        {
            Output.WriteLine($"    Symbol.This value type: {symbolThisVal.ObjectValue?.GetType().Name ?? "null"}");
            Output.WriteLine($"    Symbol.This value kind: {symbolThisVal.Kind}");
        }

        // Check slot count before script execution
        Output.WriteLine($"    GlobalEnvironment.SlotCount before: {globalEnv.SlotCount}");

        // Apply the delete to engine3 - this is what the test is actually testing
        await engine3.Evaluate("delete Array.prototype[Symbol.iterator]");

        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine3.Evaluate("""
            var [x] = [1];
            Array;
            """));

        // Dump log messages
        Output.WriteLine("  Log messages:");
        foreach (var record in logger.Collector.Snapshot())
        {
            if (record.Level >= Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Output.WriteLine($"    [{record.Level}] {record.Message}");
            }
        }

        // Check that it's a TypeError with proper properties
        string? thrownName = null;
        string? message = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty("name", out var nameVal))
            {
                thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(nameVal.ToObject()), null);
            }
            if (obj.TryGetProperty("message", out var msgVal))
            {
                message = JsOps.ToJsString(JsValue.FromObjectUnsafe(msgVal.ToObject()), null);
            }
        }

        Output.WriteLine($"Error name: {thrownName}");
        Output.WriteLine($"Error message: {message}");
        Output.WriteLine($"ThrownValue kind: {ex.ThrownValue.Kind}");
        Output.WriteLine($"ThrownValue.ObjectValue type: {ex.ThrownValue.ObjectValue?.GetType().Name ?? "null"}");

        Assert.Equal("TypeError", thrownName);
    }

    [Fact]
    public async Task ElisionDestructuringConsumesIterator()
    {
        // This mimics Test262's ary-ptrn-elision-iter-close.js test
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const iter = (function* () {
              yield 1;
              yield 2;
            })();

            function fn() {
              for (var [,] = iter; ; ) {
                return;
              }
            }

            fn();

            iter.next().done;
            """);

        Output.WriteLine($"Result: {result}");
        Output.WriteLine($"Result kind: {result?.GetType().Name}");

        // After destructuring with [,] (one elision), iter.next() should be done
        Assert.True(JsOps.ToBoolean(JsValue.FromObjectUnsafe(result)),
            $"Expected iter.next().done to be true, got {result}");
    }

    [Fact]
    public async Task DeletedArrayIteratorInForLoopThrowsTypeError()
    {
        // This mimics Test262's for loop version
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            delete Array.prototype[Symbol.iterator];
            for (var [x, y, z] = [1, 2, 3]; false; ) { }
            """));

        // Check that it's a TypeError with proper properties
        string? thrownName = null;
        string? message = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty("name", out var nameVal))
            {
                thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(nameVal.ToObject()), null);
            }
            if (obj.TryGetProperty("message", out var msgVal))
            {
                message = JsOps.ToJsString(JsValue.FromObjectUnsafe(msgVal.ToObject()), null);
            }
        }

        Output.WriteLine($"Error name: {thrownName}");
        Output.WriteLine($"Error message: {message}");
        Output.WriteLine($"ThrownValue kind: {ex.ThrownValue.Kind}");
        Output.WriteLine($"ThrownValue.ObjectValue type: {ex.ThrownValue.ObjectValue?.GetType().Name ?? "null"}");

        Assert.Equal("TypeError", thrownName);
    }

    [Theory]
    [InlineData("0, [ x ] = iterable;", 1, 0)]
    [InlineData("0, [...x] = iterable;", 1, 0)]
    public async Task ArrayAssignmentDoesNotInvokeReturnOnIteratorThrow(string assignment, int expectedNext,
        int expectedReturn)
    {
        await using var engine = CreateEngine();
        var logger = new TestLogger();
        engine.RealmState.Logger = logger;
        var result = await engine.Evaluate($$"""
            var nextCount = 0;
            var returnCount = 0;
            var iterator = {
              next: function() {
                nextCount += 1;
                throw new Error("boom");
              },
              return: function() {
                returnCount += 1;
              }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            try {
              {{assignment}}
            } catch (e) {
              // Swallow so we can observe the counters.
            }

            ({ next: nextCount, ret: returnCount });
            """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.True(obj.TryGetProperty("next", out var next), "Missing next property");
        Assert.True(obj.TryGetProperty("ret", out var ret), "Missing ret property");
        Assert.Equal(expectedNext, JsOps.ToNumber(next));
        var iteratorCloseLogs = logger.Collector.Snapshot()
            .Where(r => r.Message.Contains("IteratorClose", StringComparison.Ordinal)).ToArray();
        var logSummary = string.Join(" | ", iteratorCloseLogs.Select(r => r.Message));
        Assert.True(JsOps.ToNumber(ret) == expectedReturn,
            $"returnCount={JsOps.ToNumber(ret)}; logs={logSummary}");
        Assert.True(
            iteratorCloseLogs.Length == expectedReturn,
            logSummary);
    }

    [Fact]
    public async Task DiagnoseDeleteArrayPrototypeIterator()
    {
        await using var engine = CreateEngine();
        var realm = engine.RealmState;

        // Capture state before delete
        var arrayPrototypeBefore = realm.ArrayPrototype;
        var arrayConstructorBefore = realm.ArrayConstructor;
        Output.WriteLine($"BEFORE DELETE:");
        Output.WriteLine($"  ArrayPrototype: {arrayPrototypeBefore?.GetType().Name ?? "NULL"}");
        Output.WriteLine($"  ArrayPrototype hash: {arrayPrototypeBefore?.GetHashCode()}");
        Output.WriteLine($"  ArrayConstructor: {arrayConstructorBefore?.GetType().Name ?? "NULL"}");

        // Check if Symbol.iterator exists on Array.prototype before delete
        if (arrayPrototypeBefore is IJsPropertyAccessor proto)
        {
            var hasIterator = proto.TryGetProperty(SymbolKeys.Iterator, out var iterVal);
            Output.WriteLine($"  Has Symbol.iterator: {hasIterator}");
            Output.WriteLine($"  Symbol.iterator value: {iterVal}");
        }

        // Execute the delete
        var deleteResult = await engine.Evaluate("delete Array.prototype[Symbol.iterator]");
        Output.WriteLine($"\nDELETE RESULT: {deleteResult}");

        // Capture state after delete
        var arrayPrototypeAfter = realm.ArrayPrototype;
        var arrayConstructorAfter = realm.ArrayConstructor;
        Output.WriteLine($"\nAFTER DELETE:");
        Output.WriteLine($"  ArrayPrototype: {arrayPrototypeAfter?.GetType().Name ?? "NULL"}");
        Output.WriteLine($"  ArrayPrototype hash: {arrayPrototypeAfter?.GetHashCode()}");
        Output.WriteLine($"  ArrayConstructor: {arrayConstructorAfter?.GetType().Name ?? "NULL"}");
        Output.WriteLine($"  Same prototype object: {ReferenceEquals(arrayPrototypeBefore, arrayPrototypeAfter)}");

        // Check if Symbol.iterator exists on Array.prototype after delete
        if (arrayPrototypeAfter is IJsPropertyAccessor protoAfter)
        {
            var hasIterator = protoAfter.TryGetProperty(SymbolKeys.Iterator, out var iterVal);
            Output.WriteLine($"  Has Symbol.iterator: {hasIterator}");
        }

        // Now try to create an array and check its prototype
        Output.WriteLine($"\nCREATING NEW ARRAY:");
        var newArrayResult = await engine.Evaluate("[1, 2, 3]");
        Output.WriteLine($"  Result type: {newArrayResult?.GetType().Name ?? "null"}");

        if (newArrayResult is JsArray jsArray)
        {
            Output.WriteLine($"  JsArray.RealmState: {jsArray.RealmState?.GetType().Name ?? "NULL"}");
            Output.WriteLine($"  JsArray.RealmState.ArrayPrototype: {jsArray.RealmState?.ArrayPrototype?.GetType().Name ?? "NULL"}");

            // Check prototype chain
            if (jsArray is IPrototypeAccessorProvider provider)
            {
                var proto2 = provider.PrototypeAccessor;
                Output.WriteLine($"  JsArray prototype: {proto2?.GetType().Name ?? "NULL"}");
                Output.WriteLine($"  Same as realm ArrayPrototype: {ReferenceEquals(proto2, arrayPrototypeAfter)}");
            }
        }
        else
        {
            Output.WriteLine($"  Result is not JsArray: {newArrayResult}");
        }

        // Assertions to verify state
        Assert.NotNull(arrayPrototypeAfter);
        Assert.True(ReferenceEquals(arrayPrototypeBefore, arrayPrototypeAfter),
            "ArrayPrototype reference changed after delete!");
    }
}

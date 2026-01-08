using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class IteratorCloseGeneratorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task GeneratorCloseViaBreak_CallsFinally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var finallyCount = 0;
            function* g() {
              try {
                yield 1;
              } finally {
                finallyCount++;
              }
            }
            
            for (var x of g()) {
              break;
            }
            
            finallyCount;
            """);

        Output.WriteLine($"finallyCount: {result}");
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorCloseViaContinue_CallsFinally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var finallyCount = 0;
            function* g() {
              try {
                yield 1;
                yield 2;
              } finally {
                finallyCount++;
              }
            }
            
            outer: for (var i = 0; i < 1; i++) {
              for (var x of g()) {
                continue outer;
              }
            }
            
            finallyCount;
            """);

        Output.WriteLine($"finallyCount: {result}");
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorCloseViaThrow_CallsFinally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var finallyCount = 0;
            var caught = false;
            function* g() {
              try {
                yield 1;
              } finally {
                finallyCount++;
              }
            }
            
            try {
              for (var x of g()) {
                throw new Error('test');
              }
            } catch (e) {
              caught = true;
            }
            
            [finallyCount, caught];
            """);

        var arr = (JsTypes.JsArray)result!;
        Output.WriteLine($"finallyCount: {arr.GetElement(0)}, caught: {arr.GetElement(1)}");
        Assert.Equal(1.0, arr.GetElement(0).AsDouble());
        Assert.True(arr.GetElement(1).AsBoolean());
    }
}

public sealed class IteratorCloseGeneratorTests_Test262(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task GeneratorCloseViaBreak_Test262Style()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var startedCount = 0;
            var finallyCount = 0;
            var iterationCount = 0;
            function* values() {
              startedCount += 1;
              try {
                yield;
              } finally {
                finallyCount += 1;
              }
            }
            var iterable = values();

            for (var x of iterable) {
              iterationCount += 1;
              break;
            }

            [startedCount, finallyCount, iterationCount];
            """);

        var arr = (JsTypes.JsArray)result!;
        Output.WriteLine($"startedCount: {arr.GetElement(0)}, finallyCount: {arr.GetElement(1)}, iterationCount: {arr.GetElement(2)}");
        Assert.Equal(1.0, arr.GetElement(0).AsDouble()); // Generator started
        Assert.Equal(1.0, arr.GetElement(2).AsDouble()); // One iteration
        Assert.Equal(1.0, arr.GetElement(1).AsDouble()); // Generator closed (finally ran)
    }

    /// <summary>
    /// This test exactly matches Test262: language/statements/for-of/continue-from-catch.js
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ContinueFromCatch_ExactTest262Match()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* values() {
              yield 1;
              yield 1;
            }
            var iterator = values();
            var i = 0;

            for (var x of iterator) {
              try {
                throw new Error();
              } catch (err) {
                i++;
                continue;
              }
            }

            i;
            """);

        Output.WriteLine($"i = {result}");
        Assert.Equal(2.0, result);
    }

    /// <summary>
    /// Simpler version: continue from catch inside for-of over array
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ContinueFromCatch_ArrayIterable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var i = 0;

            for (var x of [1, 2]) {
              try {
                throw new Error();
              } catch (err) {
                i++;
                continue;
              }
            }

            i;
            """);

        Output.WriteLine($"i = {result}");
        Assert.Equal(2.0, result);
    }

    /// <summary>
    /// Simplest: continue from try block (no catch) inside for-of over generator
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ContinueFromTryBody_Generator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* values() {
              yield 1;
              yield 2;
            }
            var i = 0;

            for (var x of values()) {
              i++;
              continue;
            }

            i;
            """);

        Output.WriteLine($"i = {result}");
        Assert.Equal(2.0, result);
    }

    /// <summary>
    /// Continue without any try/catch - should work
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task SimpleContinue_Generator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* values() {
              yield 1;
              yield 2;
            }
            var i = 0;

            for (var x of values()) {
              if (x === 1) {
                i++;
                continue;
              }
              i += 10;
            }

            i;
            """);

        Output.WriteLine($"i = {result}");
        Assert.Equal(11.0, result);
    }
}

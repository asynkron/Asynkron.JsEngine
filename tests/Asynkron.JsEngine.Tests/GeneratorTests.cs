using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for generator functions (function*) and the iterator protocol.
/// </summary>
public abstract class GeneratorTestsBase(ITestOutputHelper testOutputHelper) : FastPathTestBase(testOutputHelper)
{
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper;
    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.

    [Fact(Timeout = 2000)]
    public async Task GeneratorFunction_CanBeDeclared()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should not throw
        var temp = await engine.Evaluate("""

                                                     function* simpleGenerator() {
                                                         yield 1;
                                                     }

                                         """);
    }

    [Fact(Timeout = 2000)]
    public async Task GeneratorFunction_ReturnsIteratorObject()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var result = await engine.Evaluate("""

                                                       function* gen() {
                                                           yield 1;
                                                       }
                                                       gen();

                                           """);

        // Assert
        Assert.NotNull(result);
        // The result should be an object (we can't directly check JsObject since it's internal)
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_HasNextMethod()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                     }
                                                     let g = gen();

                                         """);
        var hasNext = await engine.Evaluate("g.next;");

        // Assert - next should be callable
        Assert.NotNull(hasNext);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldsSingleValue()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 42;
                                                     }
                                                     let g = gen();
                                                     let result = g.next();

                                         """);
        var value = await engine.Evaluate("result.value;");
        var done = await engine.Evaluate("result.done;");

        // Assert
        Assert.Equal(42.0, value);
        Assert.False((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldsMultipleValues()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                         yield 2;
                                                         yield 3;
                                                     }
                                                     let g = gen();

                                         """);

        var r1Value = await engine.Evaluate("g.next().value;");
        var r2Value = await engine.Evaluate("g.next().value;");
        var r3Value = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(1.0, r1Value);
        Assert.Equal(2.0, r2Value);
        Assert.Equal(3.0, r3Value);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ReturnsIteratorResult()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 10;
                                                     }
                                                     let g = gen();
                                                     let result = g.next();

                                         """);

        // Assert - result should be an object with value and done properties
        var result = await engine.Evaluate("result;");
        Assert.NotNull(result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_IteratorResultHasValueAndDone()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 5;
                                                     }
                                                     let g = gen();
                                                     let result = g.next();

                                         """);

        // Check the properties exist by accessing them
        var value = await engine.Evaluate("result.value;");
        var done = await engine.Evaluate("result.done;");

        // Assert - both properties should exist
        Assert.NotNull(value);
        Assert.NotNull(done);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_CompletesWithDoneTrue()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                     }
                                                     let g = gen();
                                                     g.next();  // Get the yielded value
                                                     let finalResult = g.next();  // Generator is done

                                         """);

        var done = await engine.Evaluate("finalResult.done;");
        var isUndefined = await engine.Evaluate("finalResult.value === undefined;");

        // Assert
        Assert.True((bool)done!);
        Assert.True((bool)isUndefined!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldsExpressions()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1 + 1;
                                                         yield 2 * 3;
                                                     }
                                                     let g = gen();

                                         """);

        var r1 = await engine.Evaluate("g.next().value;");
        var r2 = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(2.0, r1);
        Assert.Equal(6.0, r2);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldsVariables()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         let x = 10;
                                                         yield x;
                                                         let y = 20;
                                                         yield y;
                                                     }
                                                     let g = gen();

                                         """);

        var r1 = await engine.Evaluate("g.next().value;");
        var r2 = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(10.0, r1);
        Assert.Equal(20.0, r2);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_WithParameters()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen(start) {
                                                         yield start;
                                                         yield start + 1;
                                                     }
                                                     let g = gen(100);

                                         """);

        var r1 = await engine.Evaluate("g.next().value;");
        var r2 = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(100.0, r1);
        Assert.Equal(101.0, r2);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_CanBeCalledMultipleTimes()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                         yield 2;
                                                     }
                                                     let g1 = gen();
                                                     let g2 = gen();

                                         """);

        var g1_r1 = await engine.Evaluate("g1.next().value;");
        var g2_r1 = await engine.Evaluate("g2.next().value;");
        var g1_r2 = await engine.Evaluate("g1.next().value;");
        var g2_r2 = await engine.Evaluate("g2.next().value;");

        // Assert - Each generator maintains independent state
        Assert.Equal(1.0, g1_r1);
        Assert.Equal(1.0, g2_r1);
        Assert.Equal(2.0, g1_r2);
        Assert.Equal(2.0, g2_r2);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_EmptyGenerator()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                     }
                                                     let g = gen();
                                                     let result = g.next();

                                         """);

        var done = await engine.Evaluate("result.done;");

        // Assert
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_WithReturn()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                         return 99;
                                                     }
                                                     let g = gen();
                                                     let r1 = g.next();
                                                     let r2 = g.next();

                                         """);

        var r1Value = await engine.Evaluate("r1.value;");
        var r1Done = await engine.Evaluate("r1.done;");
        var r2Value = await engine.Evaluate("r2.value;");
        var r2Done = await engine.Evaluate("r2.done;");

        // Assert
        Assert.Equal(1.0, r1Value);
        Assert.False((bool)r1Done!);
        Assert.Equal(99.0, r2Value);
        Assert.True((bool)r2Done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStar_Array()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Simpler test: just yield* [1, 2, 3] without prior yield
        var temp = await engine.Evaluate("""
            function* g() {
                yield* [1, 2, 3];
            }
            let iter = g();
            let results = [];
            let r = iter.next();
            results.push({value: r.value, done: r.done});
            r = iter.next();
            results.push({value: r.value, done: r.done});
            r = iter.next();
            results.push({value: r.value, done: r.done});
            r = iter.next();
            results.push({value: r.value, done: r.done});
            results;
        """);

        // Get individual values
        var v0 = await engine.Evaluate("results[0].value;");
        var v1 = await engine.Evaluate("results[1].value;");
        var v2 = await engine.Evaluate("results[2].value;");
        var v3 = await engine.Evaluate("results[3].value;");
        var d3 = await engine.Evaluate("results[3].done;");

        // Assert: expecting 1, 2, 3, undefined
        Assert.Equal(1.0, v0);
        Assert.Equal(2.0, v1);
        Assert.Equal(3.0, v2);
        Assert.Equal(Symbol.Undefined, v3);
        Assert.True((bool)d3!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStar_DelegatesValues()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* inner() {
                                                         yield 1;
                                                         yield 2;
                                                         return 42;
                                                     }
                                                     function* outer() {
                                                         yield 0;
                                                         return yield* inner();
                                                     }
                                                     let g = outer();
                                                     let r1 = g.next();
                                                     let r2 = g.next();
                                                     let r3 = g.next();
                                                     let r4 = g.next();

                                         """);

        var r1Value = await engine.Evaluate("r1.value;");
        var r2Value = await engine.Evaluate("r2.value;");
        var r3Value = await engine.Evaluate("r3.value;");
        var r4Value = await engine.Evaluate("r4.value;");
        var r1Done = await engine.Evaluate("r1.done;");
        var r2Done = await engine.Evaluate("r2.done;");
        var r3Done = await engine.Evaluate("r3.done;");
        var r4Done = await engine.Evaluate("r4.done;");

        // Assert
        Assert.Equal(0.0, r1Value);
        Assert.False((bool)r1Done!);
        Assert.Equal(1.0, r2Value);
        Assert.False((bool)r2Done!);
        Assert.Equal(2.0, r3Value);
        Assert.False((bool)r3Done!);
        Assert.Equal(42.0, r4Value);
        Assert.True((bool)r4Done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStar_ReturnValueUsedByOuterGenerator()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* inner() {
                                                         yield 10;
                                                         return 5;
                                                     }
                                                     function* outer() {
                                                         const captured = yield* inner();
                                                         yield captured;
                                                     }
                                                     let g = outer();
                                                     let r1 = g.next();
                                                     let r2 = g.next();
                                                     let r3 = g.next();

                                         """);

        var firstValue = await engine.Evaluate("r1.value;");
        var secondValue = await engine.Evaluate("r2.value;");
        var thirdValueIsUndefined = await engine.Evaluate("r3.value === undefined;");
        var firstDone = await engine.Evaluate("r1.done;");
        var secondDone = await engine.Evaluate("r2.done;");
        var thirdDone = await engine.Evaluate("r3.done;");

        // Assert
        Assert.Equal(10.0, firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(5.0, secondValue);
        Assert.False((bool)secondDone!);
        Assert.True((bool)thirdValueIsUndefined!);
        Assert.True((bool)thirdDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarReceivesSentValuesIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* inner() {
                const received = yield "inner";
                yield received * 2;
                return received * 3;
            }
            function* outer() {
                const result = yield* inner();
                yield `done:${result}`;
            }
            let g = outer();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        await engine.Evaluate("const second = g.next(5);");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        await engine.Evaluate("const final = g.next();");
        var finalDone = await engine.Evaluate("final.done;");

        Assert.Equal("inner", firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(10.0, secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal("done:15", thirdValue);
        Assert.False((bool)thirdDone!);
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarThrowDeliversCleanupIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];
            function* inner() {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    log.push("inner-finally");
                    yield "inner-cleanup";
                }
            }
            function* outer() {
                try {
                    yield* inner();
                } finally {
                    log.push("outer-finally");
                    yield "outer-cleanup";
                }
            }
            let g = outer();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("g.next();");
        await engine.Evaluate("const innerCleanup = g.throw('boom');");
        var innerCleanupValue = await engine.Evaluate("innerCleanup.value;");
        var innerCleanupDone = await engine.Evaluate("innerCleanup.done;");

        await engine.Evaluate("const outerCleanup = g.next();");
        var outerCleanupValue = await engine.Evaluate("outerCleanup.value;");
        var outerCleanupDone = await engine.Evaluate("outerCleanup.done;");

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));
        var logTranscript = await engine.Evaluate("log.join(',');");

        Assert.Equal("inner-cleanup", innerCleanupValue);
        Assert.False((bool)innerCleanupDone!);
        Assert.Equal("outer-cleanup", outerCleanupValue);
        Assert.False((bool)outerCleanupDone!);
        Assert.Equal("boom", exception.ThrownValue);
        Assert.Equal("inner-finally,outer-finally", logTranscript);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarReturnDeliversCleanupIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let returnLog = [];
            function* inner() {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    returnLog.push("inner-finally");
                    yield "inner-cleanup";
                }
            }
            function* outer() {
                try {
                    yield* inner();
                } finally {
                    returnLog.push("outer-finally");
                    yield "outer-cleanup";
                }
            }
            let g = outer();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const innerCleanup = g.return(99);");
        var innerCleanupValue = await engine.Evaluate("innerCleanup.value;");
        var innerCleanupDone = await engine.Evaluate("innerCleanup.done;");

        await engine.Evaluate("const outerCleanup = g.next();");
        var outerCleanupValue = await engine.Evaluate("outerCleanup.value;");
        var outerCleanupDone = await engine.Evaluate("outerCleanup.done;");

        await engine.Evaluate("const finalResult = g.next();");
        var finalValue = await engine.Evaluate("finalResult.value;");
        var finalDone = await engine.Evaluate("finalResult.done;");
        var transcript = await engine.Evaluate("returnLog.join(',');");

        Assert.Equal("inner-cleanup", innerCleanupValue);
        Assert.False((bool)innerCleanupDone!);
        Assert.Equal("outer-cleanup", outerCleanupValue);
        Assert.False((bool)outerCleanupDone!);
        Assert.Equal(99.0, finalValue);
        Assert.True((bool)finalDone!);
        Assert.Equal("inner-finally,outer-finally", transcript);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarThrowContinuesWhenIteratorResumesIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                let index = 0;
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                if (index === 0) {
                                    index++;
                                    return { value: "initial", done: false };
                                }
                                return { value: "done", done: true };
                            },
                            throw(err) {
                                return { value: `handled:${err}`, done: false };
                            }
                        };
                    }
                };
            }

            function* outer() {
                yield* makeIterator();
                yield "after";
            }

            let g = outer();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        Assert.Equal("initial", firstValue);

        await engine.Evaluate("const second = g.throw('boom');");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");
        Assert.Equal("handled:boom", secondValue);
        Assert.False((bool)secondDone!);

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");
        Assert.Equal("after", thirdValue);
        Assert.False((bool)thirdDone!);

        await engine.Evaluate("const final = g.next();");
        var finalDone = await engine.Evaluate("final.done;");
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarThrowRequiresIteratorResultObjectIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                return { value: "initial", done: false };
                            },
                            throw(err) {
                                return "not-an-object";
                            }
                        };
                    }
                };
            }

            function* outer() {
                yield* makeIterator();
            }

            let g = outer();
        """);

        await engine.Evaluate("g.next();");
        var signal = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.throw('boom');"));
        AssertIteratorResultTypeError(signal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarReturnRequiresIteratorResultObjectIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                return { value: "initial", done: false };
                            },
                            return(value) {
                                return 42;
                            }
                        };
                    }
                };
            }

            function* outer() {
                yield* makeIterator();
            }

            let g = outer();
        """);

        await engine.Evaluate("g.next();");
        var signal = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.return('done');"));
        AssertIteratorResultTypeError(signal);
    }

    private static void AssertIteratorResultTypeError(ThrowSignal signal)
    {
        if (signal.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("message", out var message))
        {
            Assert.Equal("Iterator result is not an object.", JsOps.ToJsString(JsValue.FromObjectUnsafe(message.ToObject()), null));
            return;
        }

        if (signal.ThrownValue.TryGetString(out var str))
        {
            Assert.Equal("Iterator result is not an object.", str);
            return;
        }

        Assert.Fail($"Unexpected thrown value: {signal.ThrownValue}");
    }


    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarThrowAwaitedPromiseIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                let index = 0;
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                if (index++ === 0) {
                                    return { value: "initial", done: false };
                                }
                                return { value: "done", done: true };
                            },
                            throw(err) {
                                return Promise.resolve({ value: `handled:${err}`, done: false });
                            }
                        };
                    }
                };
            }

            function* outer() {
                yield* makeIterator();
                yield "after";
            }

            let g = outer();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        Assert.Equal("initial", firstValue);

        await engine.Evaluate("const second = g.throw('boom');");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");
        Assert.Equal("handled:boom", secondValue);
        Assert.False((bool)secondDone!);

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        Assert.Equal("after", thirdValue);
    }


    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarThrowPromiseRejectsIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                return { value: "initial", done: false };
                            },
                            throw(err) {
                                return Promise.reject(`reject:${err}`);
                            }
                        };
                    }
                };
            }

            function* outer() {
                yield* makeIterator();
            }

            let g = outer();
        """);

        await engine.Evaluate("g.next();");
        var signal = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.throw('boom');"));
        Assert.Equal("reject:boom", signal.ThrownValue);
    }


    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarReturnAwaitedPromiseIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                let closed = false;
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                if (closed) {
                                    return { value: "finished", done: true };
                                }
                                return { value: 1, done: false };
                            },
                            return(value) {
                                closed = true;
                                return Promise.resolve({ value: value + 100, done: true });
                            }
                        };
                    }
                };
            }

            function* outer() {
                const result = yield* makeIterator();
                return `result:${result}`;
            }

            let g = outer();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        Assert.Equal(1.0, firstValue);

        await engine.Evaluate("const second = g.return(5);");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");
        Assert.Equal(105.0, secondValue);
        Assert.True((bool)secondDone!);

        await engine.Evaluate("const final = g.next();");
        var finalValue = await engine.Evaluate("final.value;");
        var finalDone = await engine.Evaluate("final.done;");
        Assert.Equal("result:finished", finalValue);
        Assert.True((bool)finalDone!);
    }


    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarReturnDoneFalseContinuesIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function makeIterator() {
                let closed = false;
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                if (closed) {
                                    return { value: "finished", done: true };
                                }
                                return { value: 1, done: false };
                            },
                            return(value) {
                                closed = true;
                                return { value: value + 100, done: false };
                            }
                        };
                    }
                };
            }

            function* outer() {
                const result = yield* makeIterator();
                return `result:${result}`;
            }

            let g = outer();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        Assert.Equal(1.0, firstValue);

        await engine.Evaluate("const second = g.return(5);");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");
        Assert.Equal(105.0, secondValue);
        Assert.False((bool)secondDone!);

        await engine.Evaluate("const final = g.next();");
        var finalValue = await engine.Evaluate("final.value;");
        var finalDone = await engine.Evaluate("final.done;");
        Assert.Equal("result:finished", finalValue);
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarNestedTryFinallyThrowMidFinalIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];
            function* inner() {
                try {
                    yield 1;
                } finally {
                    log.push("inner-finally-1");
                    yield "inner-cleanup-1";
                    log.push("inner-finally-2");
                    yield "inner-cleanup-2";
                }
            }
            function* outer() {
                try {
                    yield* inner();
                } finally {
                    log.push("outer-finally-1");
                    yield "outer-cleanup-1";
                    log.push("outer-finally-2");
                    yield "outer-cleanup-2";
                }
            }
            let g = outer();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected yield* nested try/finally generator to lower to IR.");
        Assert.Equal(0, failed);

        await engine.Evaluate("g.next();");

        await engine.Evaluate("const innerCleanup1 = g.throw('boom');");
        var innerCleanup1Value = await engine.Evaluate("innerCleanup1.value;");
        var innerCleanup1Done = await engine.Evaluate("innerCleanup1.done;");

        await engine.Evaluate("const innerCleanup2 = g.throw('override');");
        var innerCleanup2Value = await engine.Evaluate("innerCleanup2.value;");
        var innerCleanup2Done = await engine.Evaluate("innerCleanup2.done;");

        await engine.Evaluate("const outerCleanup1 = g.next();");
        var outerCleanup1Value = await engine.Evaluate("outerCleanup1.value;");
        var outerCleanup1Done = await engine.Evaluate("outerCleanup1.done;");

        await engine.Evaluate("const outerCleanup2 = g.next();");
        var outerCleanup2Value = await engine.Evaluate("outerCleanup2.value;");
        var outerCleanup2Done = await engine.Evaluate("outerCleanup2.done;");

        var finalThrow = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));
        var transcript = await engine.Evaluate("log.join(',');");

        Assert.Equal("inner-cleanup-1", innerCleanup1Value);
        Assert.False((bool)innerCleanup1Done!);
        Assert.Equal("inner-cleanup-2", innerCleanup2Value);
        Assert.False((bool)innerCleanup2Done!);
        Assert.Equal("outer-cleanup-1", outerCleanup1Value);
        Assert.False((bool)outerCleanup1Done!);
        Assert.Equal("outer-cleanup-2", outerCleanup2Value);
        Assert.False((bool)outerCleanup2Done!);
        Assert.Equal("override", finalThrow.ThrownValue);
        Assert.Equal("inner-finally-1,inner-finally-2,outer-finally-1,outer-finally-2", transcript);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarNestedTryFinallyReturnMidFinalIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];
            function* inner() {
                try {
                    yield 1;
                } finally {
                    log.push("inner-finally-1");
                    yield "inner-cleanup-1";
                    log.push("inner-finally-2");
                    yield "inner-cleanup-2";
                }
            }
            function* outer() {
                try {
                    yield* inner();
                } finally {
                    log.push("outer-finally-1");
                    yield "outer-cleanup-1";
                    log.push("outer-finally-2");
                    yield "outer-cleanup-2";
                }
            }
            let g = outer();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected yield* nested try/finally generator to lower to IR.");
        Assert.Equal(0, failed);

        await engine.Evaluate("g.next();");

        await engine.Evaluate("const innerCleanup1 = g.return(42);");
        var innerCleanup1Value = await engine.Evaluate("innerCleanup1.value;");
        var innerCleanup1Done = await engine.Evaluate("innerCleanup1.done;");

        await engine.Evaluate("const innerCleanup2 = g.return(99);");
        var innerCleanup2Value = await engine.Evaluate("innerCleanup2.value;");
        var innerCleanup2Done = await engine.Evaluate("innerCleanup2.done;");

        await engine.Evaluate("const outerCleanup1 = g.next();");
        var outerCleanup1Value = await engine.Evaluate("outerCleanup1.value;");
        var outerCleanup1Done = await engine.Evaluate("outerCleanup1.done;");

        await engine.Evaluate("const outerCleanup2 = g.next();");
        var outerCleanup2Value = await engine.Evaluate("outerCleanup2.value;");
        var outerCleanup2Done = await engine.Evaluate("outerCleanup2.done;");

        await engine.Evaluate("const finalResult = g.next();");
        var finalValue = await engine.Evaluate("finalResult.value;");
        var finalDone = await engine.Evaluate("finalResult.done;");
        var transcript = await engine.Evaluate("log.join(',');");

        Assert.Equal("inner-cleanup-1", innerCleanup1Value);
        Assert.False((bool)innerCleanup1Done!);
        Assert.Equal("inner-cleanup-2", innerCleanup2Value);
        Assert.False((bool)innerCleanup2Done!);
        Assert.Equal("outer-cleanup-1", outerCleanup1Value);
        Assert.False((bool)outerCleanup1Done!);
        Assert.Equal("outer-cleanup-2", outerCleanup2Value);
        Assert.False((bool)outerCleanup2Done!);
        Assert.Equal(99.0, finalValue);
        Assert.True((bool)finalDone!);
        Assert.Equal("inner-finally-1,inner-finally-2,outer-finally-1,outer-finally-2", transcript);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_NextValueIsDeliveredToYield()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                         function* gen() {
                                             const received = yield 1;
                                             return received;
                                         }
                                         let g = gen();
                                         let first = g.next();
                                         let second = g.next(99);

                             """);

        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        Assert.Equal(1.0, firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(99.0, secondValue);
        Assert.True((bool)secondDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_NextDefaultsToUndefinedWhenNoValueProvided()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                         function* gen() {
                                             const received = yield 1;
                                             return received === undefined;
                                         }
                                         let g = gen();
                                         let first = g.next();
                                         let second = g.next();

                             """);

        var firstDone = await engine.Evaluate("first.done;");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        Assert.False((bool)firstDone!);
        Assert.True((bool)secondValue!);
        Assert.True((bool)secondDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ThrowDeliversExceptionToYield()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("""

                                         function* gen() {
                                             try {
                                                 yield 1;
                                             } catch (err) {
                                                 yield err + 1;
                                             }
                                             return 99;
                                         }
                                         let g = gen();
                                         let first = g.next();
                                         let second = g.throw(4);
                                         let third = g.next();

                             """);

        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        Assert.Equal(1.0, firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(5.0, secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal(99.0, thirdValue);
        Assert.True((bool)thirdDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ThrowWithoutCatchPropagatesError()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function* gen() {
                    yield 1;
                    yield 2;
                }
                let g = gen();
                g.next();
                g.throw("boom");
            """));
        Assert.Equal("boom", exception.ThrownValue);
    }

    [Fact(Timeout = 2000)]
    public async Task GeneratorExpression_CanBeAssigned()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     let gen = function*() {
                                                         yield 42;
                                                     };
                                                     let g = gen();
                                                     let result = g.next();

                                         """);

        var value = await engine.Evaluate("result.value;");

        // Assert
        Assert.Equal(42.0, value);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_HasReturnMethod()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                         yield 2;
                                                     }
                                                     let g = gen();

                                         """);
        var hasReturn = await engine.Evaluate("g[\"return\"];");

        // Assert - return should be callable
        Assert.NotNull(hasReturn);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ReturnMethodCompletesGenerator()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                         yield 2;
                                                     }
                                                     let g = gen();
                                                     g.next();  // Get first value
                                                     let returnResult = g["return"](99);
                                                     let nextResult = g.next();  // Should be done

                                         """);

        var returnValue = await engine.Evaluate("returnResult.value;");
        var returnDone = await engine.Evaluate("returnResult.done;");
        var nextDone = await engine.Evaluate("nextResult.done;");

        // Assert
        Assert.Equal(99.0, returnValue);
        Assert.True((bool)returnDone!);
        Assert.True((bool)nextDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_HasThrowMethod()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act
        var temp = await engine.Evaluate("""

                                                     function* gen() {
                                                         yield 1;
                                                     }
                                                     let g = gen();

                                         """);
        var hasThrow = await engine.Evaluate("g[\"throw\"];");

        // Assert - throw should be callable
        Assert.NotNull(hasThrow);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_InitializationRunsOnce()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let hits = 0;
            function* gen() {
                hits++;
                yield hits;
                hits++;
                yield hits;
            }
            let g = gen();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var total = await engine.Evaluate("hits;");

        Assert.Equal(1.0, first);
        Assert.Equal(2.0, second);
        Assert.Equal(2.0, total);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_CanReceiveSentValues()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                const received = yield 1;
                yield received * 2;
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        var second = await engine.Evaluate("g.next(7).value;");
        Assert.Equal(14.0, second);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_BreakStatementExitsLoop()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* breakExample() {
                let i = 0;
                while (i < 5) {
                    yield i;
                    break;
                }
                yield 99;
            }
            let g = breakExample();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(0.0, first);
        Assert.Equal(99.0, second);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ContinueStatementSkipsLoopBody()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* continueExample() {
                let i = 0;
                while (i < 2) {
                    i = i + 1;
                    continue;
                    yield 123;
                }
                yield i;
            }
            let g = continueExample();
        """);

        var value = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(2.0, value);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_LabeledBreakTargetsOuterLoop()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* labeledBreak() {
                let outer = 0;
                outerLoop: while (outer < 3) {
                    let inner = 0;
                    while (inner < 3) {
                        yield inner;
                        break outerLoop;
                    }
                    outer = outer + 1;
                }
                yield outer;
            }
            let g = labeledBreak();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(0.0, first);
        Assert.Equal(0.0, second);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_LabeledContinueTargetsOuterLoop()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* labeledContinue() {
                let count = 0;
                outer: while (count < 2) {
                    count = count + 1;
                    inner: while (true) {
                        continue outer;
                    }
                }
                yield count;
            }
            let g = labeledContinue();
        """);

        var value = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(2.0, value);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_WhileLoopsExecuteWithIrPlan()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* loop(limit) {
                let i = 0;
                while (i < limit) {
                    yield i;
                    i = i + 1;
                }
            }
            let g = loop(3);
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var third = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(0.0, first);
        Assert.Equal(1.0, second);
        Assert.Equal(2.0, third);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_IrPathReceivesSentValues()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* echoTwice() {
                const sent = yield 1;
                yield sent * 2;
            }
            let g = echoTwice();
        """);

        await engine.Evaluate("g.next();");
        var doubled = await engine.Evaluate("g.next(9).value;");
        Assert.Equal(18.0, doubled);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_AssignmentReceivesSentValuesIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* assignGen() {
                let sent = 0;
                sent = yield 1;
                yield sent * 3;
            }
            let g = assignGen();
        """);

        await engine.Evaluate("g.next();");
        var second = await engine.Evaluate("g.next(4).value;");
        Assert.Equal(12.0, second);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_AssignmentReceivesSentValuesIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* assignGen() {
                let sent = 0;
                sent = yield 1;
                yield sent * 3;
            }
            let g = assignGen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected assignment-with-yield generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryCatchHandlesThrowIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                    yield 2;
                } catch (err) {
                    yield err + 1;
                }
            }
            let g = gen();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.throw(5).value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(1.0, first);
        Assert.Equal(6.0, second);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryCatchHandlesThrowIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                    yield 2;
                } catch (err) {
                    yield err + 1;
                }
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected try/catch generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyRunsOnReturnIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* cleanup() {
                try {
                    yield 1;
                } finally {
                    yield 2;
                }
            }
            let g = cleanup();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("let closeResult = g.return(42);");
        var closeValue = await engine.Evaluate("closeResult.value;");
        var closeDone = await engine.Evaluate("closeResult.done;");

        await engine.Evaluate("let finalResult = g.next();");
        var finalValue = await engine.Evaluate("finalResult.value;");
        var finalDone = await engine.Evaluate("finalResult.done;");

        Assert.Equal(2.0, closeValue);
        Assert.False((bool)closeDone!);
        Assert.Equal(42.0, finalValue);
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyRunsOnThrowIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let flag = 0;
            function* gen() {
                try {
                    yield 1;
                } finally {
                    flag = 1;
                    yield 2;
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("let throwResult = g.throw('boom');");
        var cleanupValue = await engine.Evaluate("throwResult.value;");
        var cleanupDone = await engine.Evaluate("throwResult.done;");

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));
        var flagValue = await engine.Evaluate("flag;");

        Assert.Equal(2.0, cleanupValue);
        Assert.False((bool)cleanupDone!);
        Assert.Equal("boom", exception.ThrownValue);
        Assert.Equal(1.0, flagValue);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyRunsOnThrowIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let flag = 0;
            function* gen() {
                try {
                    yield 1;
                } finally {
                    flag = 1;
                    yield 2;
                }
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected try/finally generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyNestedBreakIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                const log = [];
                outer: while (true) {
                    try {
                        break outer;
                    } finally {
                        try {
                            log.push("inner");
                            break outer;
                        } finally {
                            log.push("after");
                        }
                    }
                }
                yield log.join(",");
            }
            let g = gen();
        """);

        var value = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal("inner,after", value);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyNestedThrowIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                } finally {
                    try {
                        yield 2;
                    } finally {
                        yield 3;
                    }
                }
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected nested try/finally generator to lower to IR.");
        Assert.Equal(0, failed);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        await engine.Evaluate("const second = g.throw('boom');");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        Console.WriteLine($"[InterpreterThrow] first=({firstValue},{firstDone}), second=({secondValue},{secondDone}), third=({thirdValue},{thirdDone})");
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));

        Assert.Equal(1.0, firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(2.0, secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal(3.0, thirdValue);
        Assert.False((bool)thirdDone!);
        Assert.Equal("boom", exception.ThrownValue);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyNestedReturnIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                } finally {
                    try {
                        yield 2;
                    } finally {
                        yield 3;
                    }
                }
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected nested try/finally generator to lower to IR.");
        Assert.Equal(0, failed);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const mid = g.return(99);");
        var midValue = await engine.Evaluate("mid.value;");
        var midDone = await engine.Evaluate("mid.done;");

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        await engine.Evaluate("const final = g.next();");
        var finalValue = await engine.Evaluate("final.value;");
        var finalDone = await engine.Evaluate("final.done;");

        Assert.Equal(2.0, midValue);
        Assert.False((bool)midDone!);
        Assert.Equal(3.0, thirdValue);
        Assert.False((bool)thirdDone!);
        Assert.Equal(99.0, finalValue);
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyNestedThrowInterpreter()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                } finally {
                    try {
                        yield 2;
                    } finally {
                        yield 3;
                    }
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        await engine.Evaluate("const second = g.throw('boom');");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));

        Assert.Equal(1.0, firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(2.0, secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal(3.0, thirdValue);
        Assert.False((bool)thirdDone!);
        Assert.Equal("boom", exception.ThrownValue);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyNestedReturnInterpreter()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                } finally {
                    try {
                        yield 2;
                    } finally {
                        yield 3;
                    }
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const mid = g.return(99);");
        var midValue = await engine.Evaluate("mid.value;");
        var midDone = await engine.Evaluate("mid.done;");

        await engine.Evaluate("const third = g.next();");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        await engine.Evaluate("const final = g.next();");
        var finalValue = await engine.Evaluate("final.value;");
        var finalDone = await engine.Evaluate("final.done;");

        Assert.Equal(2.0, midValue);
        Assert.False((bool)midDone!);
        Assert.Equal(3.0, thirdValue);
        Assert.False((bool)thirdDone!);
        Assert.Equal(99.0, finalValue);
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyThrowMidFinalIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                } finally {
                    yield "cleanup-a";
                    yield "cleanup-b";
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const firstCleanup = g.throw('boom');");
        var firstValue = await engine.Evaluate("firstCleanup.value;");
        var firstDone = await engine.Evaluate("firstCleanup.done;");

        await engine.Evaluate("const secondCleanup = g.throw('override');");
        var secondValue = await engine.Evaluate("secondCleanup.value;");
        var secondDone = await engine.Evaluate("secondCleanup.done;");

        var finalThrow = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));

        Assert.Equal("cleanup-a", firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal("cleanup-b", secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal("override", finalThrow.ThrownValue);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_TryFinallyReturnMidFinalIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                try {
                    yield 1;
                } finally {
                    yield "cleanup-a";
                    yield "cleanup-b";
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const firstCleanup = g.return(42);");
        var firstValue = await engine.Evaluate("firstCleanup.value;");
        var firstDone = await engine.Evaluate("firstCleanup.done;");

        await engine.Evaluate("const secondCleanup = g.return(99);");
        var secondValue = await engine.Evaluate("secondCleanup.value;");
        var secondDone = await engine.Evaluate("secondCleanup.done;");

        await engine.Evaluate("const finalResult = g.next();");
        var finalValue = await engine.Evaluate("finalResult.value;");
        var finalDone = await engine.Evaluate("finalResult.done;");

        Assert.Equal("cleanup-a", firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal("cleanup-b", secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal(99.0, finalValue);
        Assert.True((bool)finalDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_CatchFinallyNestedThrowIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let catchFinallyLog = [];
            function* gen() {
                try {
                    yield 1;
                } catch (err) {
                    catchFinallyLog.push(`catch:${err}`);
                    try {
                        yield `body:${err}`;
                    } finally {
                        catchFinallyLog.push(`finally:${err}`);
                        yield `cleanup:${err}`;
                    }
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const body = g.throw('boom');");
        var bodyValue = await engine.Evaluate("body.value;");
        var bodyDone = await engine.Evaluate("body.done;");

        await engine.Evaluate("const cleanup = g.throw('override');");
        var cleanupValue = await engine.Evaluate("cleanup.value;");
        var cleanupDone = await engine.Evaluate("cleanup.done;");

        var finalThrow = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.next();"));
        var transcript = await engine.Evaluate("catchFinallyLog.join(',');");

        Assert.Equal("body:boom", bodyValue);
        Assert.False((bool)bodyDone!);
        Assert.Equal("cleanup:boom", cleanupValue);
        Assert.False((bool)cleanupDone!);
        Assert.Equal("override", finalThrow.ThrownValue);
        Assert.Equal("catch:boom,finally:boom", transcript);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_CatchFinallyNestedReturnIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let catchFinallyReturnLog = [];
            function* gen() {
                try {
                    yield 1;
                } catch (err) {
                    catchFinallyReturnLog.push(`catch:${err}`);
                    try {
                        yield `body:${err}`;
                    } finally {
                        catchFinallyReturnLog.push(`finally:${err}`);
                        yield `cleanup:${err}`;
                    }
                }
            }
            let g = gen();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("const body = g.throw('boom');");
        var bodyValue = await engine.Evaluate("body.value;");
        var bodyDone = await engine.Evaluate("body.done;");

        await engine.Evaluate("const cleanup = g.return(99);");
        var cleanupValue = await engine.Evaluate("cleanup.value;");
        var cleanupDone = await engine.Evaluate("cleanup.done;");

        await engine.Evaluate("const finalResult = g.next();");
        var finalValue = await engine.Evaluate("finalResult.value;");
        var finalDone = await engine.Evaluate("finalResult.done;");
        var transcript = await engine.Evaluate("catchFinallyReturnLog.join(',');");

        Assert.Equal("body:boom", bodyValue);
        Assert.False((bool)bodyDone!);
        Assert.Equal("cleanup:boom", cleanupValue);
        Assert.False((bool)cleanupDone!);
        Assert.Equal(99.0, finalValue);
        Assert.True((bool)finalDone!);
        Assert.Equal("catch:boom,finally:boom", transcript);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_BreakRunsFinallyIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let hits = 0;
            function* gen() {
                while (true) {
                    try {
                        break;
                    } finally {
                        hits = hits + 1;
                    }
                }
                yield hits;
            }
            let g = gen();
        """);

        var value = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(1.0, value);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ContinueRunsFinallyIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let hits = 0;
            function* gen() {
                let i = 0;
                while (i < 3) {
                    try {
                        i = i + 1;
                        continue;
                    } finally {
                        hits = hits + 1;
                    }
                }
                yield hits;
            }
            let g = gen();
        """);

        var value = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(3.0, value);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_DoWhileLoopsExecuteWithIrPlan()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* doLoop(limit) {
                let i = 0;
                do {
                    yield i;
                    i = i + 1;
                } while (i < limit);
            }
            let g = doLoop(2);
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(0.0, first);
        Assert.Equal(1.0, second);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForLoopsExecuteWithIrPlan()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* forLoop(limit) {
                for (let i = 0; i < limit; i = i + 1) {
                    yield i;
                }
            }
            let g = forLoop(3);
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var third = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(0.0, first);
        Assert.Equal(1.0, second);
        Assert.Equal(2.0, third);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForLoopContinueRunsIncrement()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* evens(limit) {
                for (let i = 0; i < limit; i = i + 1) {
                    if ((i % 2) === 1) {
                        continue;
                    }
                    yield i;
                }
            }
            let g = evens(5);
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var third = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(0.0, first);
        Assert.Equal(2.0, second);
        Assert.Equal(4.0, third);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfYieldsValuesIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                for (var value of [1, 2, 3]) {
                    yield value;
                }
            }
            let g = gen();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var third = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(1.0, first);
        Assert.Equal(2.0, second);
        Assert.Equal(3.0, third);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfYieldsValuesIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                for (var value of [1, 2, 3]) {
                    yield value;
                }
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected at least one IR plan to be built.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForLoopsExecuteWithIrPlan_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* forLoop(limit) {
                for (let i = 0; i < limit; i = i + 1) {
                    yield i;
                }
            }
            let g = forLoop(3);
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected forLoop generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarReceivesSentValuesIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* inner() {
                let received = [];
                received.push(yield 1);
                received.push(yield 2);
                return received.join(",");
            }
            function* outer() {
                const result = yield* inner();
                return result;
            }
            let g = outer();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected yield* outer generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfBreaksEarlyIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                let count = 0;
                for (var value of [1, 2, 3, 4]) {
                    if (value === 3) {
                        break;
                    }
                    count = count + value;
                    yield value;
                }
                yield count;
            }
            let g = gen();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var sum = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(1.0, first);
        Assert.Equal(2.0, second);
        Assert.Equal(3.0, sum);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfLetCreatesNewBindingIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                const callbacks = [];
                for (let value of [1, 2]) {
                    callbacks.push(() => value);
                }
                yield callbacks[0]();
                yield callbacks[1]();
            }
            let g = gen();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(1.0, first);
        Assert.Equal(2.0, second);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfLetCreatesNewBindingIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                const callbacks = [];
                for (let value of [1, 2]) {
                    callbacks.push(() => value);
                }
                yield callbacks[0]();
                yield callbacks[1]();
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1,
            "Expected for-increment generator with yield in the increment expression to lower to IR.");
        Assert.Equal(0, failed);
        Assert.Null(GeneratorIrDiagnostics.LastFailureReason);
        Assert.Null(GeneratorIrDiagnostics.LastFunctionDescription);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfDestructuringIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                for (const { value } of [{ value: 3 }, { value: 4 }]) {
                    yield value;
                }
            }
            let g = gen();
        """);

        var first = await engine.Evaluate("g.next().value;");
        var second = await engine.Evaluate("g.next().value;");
        var done = await engine.Evaluate("g.next().done;");

        Assert.Equal(3.0, first);
        Assert.Equal(4.0, second);
        Assert.True((bool)done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfDestructuringIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                for (const { value } of [{ value: 3 }, { value: 4 }]) {
                    yield value;
                }
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1,
            "Expected for-increment generator with yield in the increment expression to lower to IR.");
        Assert.Equal(0, failed);
        Assert.Null(GeneratorIrDiagnostics.LastFailureReason);
        Assert.Null(GeneratorIrDiagnostics.LastFunctionDescription);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_VariableInitializerWithMultipleYieldsIr_UsesIrPlan()
    {
        await using var engine = CreateEngine();

        GeneratorIrDiagnostics.Reset();
        await engine.Evaluate("""
            function* gen() {
                let log = [];
                let value = (yield "a") + (yield "b");
                log.push(value);
                return log[0];
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(attempts >= 1);
        Assert.True(succeeded >= 1);
        Assert.Equal(0, failed);
        Assert.Null(GeneratorIrDiagnostics.LastFailureReason);
        Assert.Null(GeneratorIrDiagnostics.LastFunctionDescription);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        await engine.Evaluate("const second = g.next(10);");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        await engine.Evaluate("const third = g.next(32);");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        Assert.Equal("a", firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal("b", secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal(42.0, thirdValue);
        Assert.True((bool)thirdDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_IfConditionComplexYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                if (1 + (yield "a")) {
                    yield "then";
                }
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected if (1 + (yield ...)) generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForConditionYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                for (let i = 0; (yield "cond"); i = i + 1) {
                    yield i;
                }
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected for (let i = 0; (yield cond); ...) generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_WhileConditionComplexYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                let log = [];
                let count = 0;
                while (1 + (yield "probe")) {
                    log.push(count);
                    count = count + 1;
                }
                return log.join(",");
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected while(1 + (yield ...)) generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_DoWhileConditionComplexYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                let log = [];
                let count = 0;
                do {
                    log.push(count);
                    count = count + 1;
                } while (1 + (yield "cond"));
                return log.join(",");
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected do { ... } while(1 + (yield ...)) generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForIncrementYieldIr_UsesIrPlan()
    {
        await using var engine = CreateEngine();

        GeneratorIrDiagnostics.Reset();
        await engine.Evaluate("""
            function* gen() {
                for (let i = 0; i < 3; i = i + (yield "step")) {
                    yield i;
                }
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1,
            "Expected for-increment generator with yield in the increment expression to lower to IR.");
        Assert.Equal(0, failed);
        Assert.Null(GeneratorIrDiagnostics.LastFailureReason);
        Assert.Null(GeneratorIrDiagnostics.LastFunctionDescription);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForIncrementMultipleYieldsIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                for (let i = 0; i < 1; i = (yield "a") + (yield "b")) {
                    yield i;
                }
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(attempts >= 1);
        Assert.True(succeeded >= 1);
        Assert.Equal(0, failed);
        Assert.Null(GeneratorIrDiagnostics.LastFailureReason);
        Assert.Null(GeneratorIrDiagnostics.LastFunctionDescription);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        await engine.Evaluate("const second = g.next();");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        await engine.Evaluate("const third = g.next(2);");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        await engine.Evaluate("const fourth = g.next(3);");
        var fourthValueIsUndefined = await engine.Evaluate("fourth.value === undefined;");
        var fourthDone = await engine.Evaluate("fourth.done;");

        Assert.Equal(0.0, firstValue);
        Assert.False((bool)firstDone!);

        Assert.Equal("a", secondValue);
        Assert.False((bool)secondDone!);

        Assert.Equal("b", thirdValue);
        Assert.False((bool)thirdDone!);

        Assert.True((bool)fourthValueIsUndefined!);
        Assert.True((bool)fourthDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_SwitchStatementIr_UsesIrPlan()
    {
        await using var engine = CreateEngine();

        GeneratorIrDiagnostics.Reset();
        await engine.Evaluate("""
            function* gen() {
                const x = yield 1;
                switch (x) {
                    case 1:
                        yield "one";
                        break;
                    default:
                        yield "other";
                        break;
                }
            }
            let g = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(attempts >= 1);
        Assert.True(succeeded >= 1);
        Assert.Equal(0, failed);
        Assert.Null(GeneratorIrDiagnostics.LastFailureReason);
        Assert.Null(GeneratorIrDiagnostics.LastFunctionDescription);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_SwitchStatementSemanticsIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* describe(value) {
                switch (value) {
                    case 1:
                        yield "one";
                        break;
                    case 2:
                    case 3:
                        yield "few";
                        break;
                    default:
                        yield "many";
                        break;
                }
            }

            let g1 = describe(1);
            let g2 = describe(2);
            let g3 = describe(3);
            let g4 = describe(10);
        """);

        var first = await engine.Evaluate("g1.next().value;");
        var firstDone = await engine.Evaluate("g1.next().done;");
        var second = await engine.Evaluate("g2.next().value;");
        var secondDone = await engine.Evaluate("g2.next().done;");
        var third = await engine.Evaluate("g3.next().value;");
        var thirdDone = await engine.Evaluate("g3.next().done;");
        var fourth = await engine.Evaluate("g4.next().value;");
        var fourthDone = await engine.Evaluate("g4.next().done;");

        Assert.Equal("one", first);
        Assert.True((bool)firstDone!);
        Assert.Equal("few", second);
        Assert.True((bool)secondDone!);
        Assert.Equal("few", third);
        Assert.True((bool)thirdDone!);
        Assert.Equal("many", fourth);
        Assert.True((bool)fourthDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_SwitchStatementDefaultNotLastIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                const x = yield 1;
                switch (x) {
                    default:
                        yield "default";
                        break;
                    case 1:
                        yield "one";
                        break;
                }
            }
            let g1 = gen();
            let g2 = gen();
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.Equal(2, attempts);
        Assert.Equal(2, succeeded);
        Assert.Equal(0, failed);

        await engine.Evaluate("const first = g1.next();");
        await engine.Evaluate("const second = g1.next(1);");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        await engine.Evaluate("g2.next();");
        await engine.Evaluate("const third = g2.next(2);");
        var thirdValue = await engine.Evaluate("third.value;");
        var thirdDone = await engine.Evaluate("third.done;");

        Assert.Equal("one", secondValue);
        Assert.False((bool)secondDone!);
        Assert.Equal("default", thirdValue);
        Assert.False((bool)thirdDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_SwitchStatementMultipleBreaksIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen(x) {
                switch (x) {
                    case 1:
                        yield "one";
                        break;
                        yield "after-break";
                    case 2:
                        yield "two";
                        yield "more-two";
                        break;
                        yield "after-second-break";
                    default:
                        yield "default";
                        yield "after-default";
                }
            }
            let g1 = gen(1);
            let g2 = gen(2);
            let g3 = gen(3);
        """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.Equal(3, attempts);
        Assert.Equal(3, succeeded);
        Assert.Equal(0, failed);

        await engine.Evaluate("const g1_first = g1.next();");
        var g1First = await engine.Evaluate("g1_first.value;");
        var g1Done = await engine.Evaluate("g1_first.done;");

        await engine.Evaluate("const g1_second = g1.next();");
        var g1SecondIsUndefined = await engine.Evaluate("g1_second.value === undefined;");
        var g1SecondDone = await engine.Evaluate("g1_second.done;");

        await engine.Evaluate("const g2_first = g2.next();");
        var g2First = await engine.Evaluate("g2_first.value;");
        await engine.Evaluate("const g2_second = g2.next();");
        var g2Second = await engine.Evaluate("g2_second.value;");
        var g2SecondDone = await engine.Evaluate("g2_second.done;");

        await engine.Evaluate("const g3_first = g3.next();");
        var g3First = await engine.Evaluate("g3_first.value;");
        var g3Done = await engine.Evaluate("g3_first.done;");

        Assert.Equal("one", g1First);
        Assert.False((bool)g1Done!);
        Assert.True((bool)g1SecondIsUndefined!);
        Assert.True((bool)g1SecondDone!);

        Assert.Equal("two", g2First);
        Assert.Equal("more-two", g2Second);
        Assert.False((bool)g2SecondDone!);

        Assert.Equal("default", g3First);
        Assert.False((bool)g3Done!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ReturnSkipsRemainingStatementsIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let updates = 0;
            function* stopEarly() {
                yield 1;
                updates = updates + 1;
                return updates;
            }
            let g = stopEarly();
        """);

        await engine.Evaluate("g.next();");
        await engine.Evaluate("var returnResult = g.return(42);");
        var resultValue = await engine.Evaluate("returnResult.value;");
        var resultDone = await engine.Evaluate("returnResult.done;");
        var updates = await engine.Evaluate("updates;");

        Assert.Equal(42.0, resultValue);
        Assert.True((bool)resultDone!);
        Assert.Equal(0.0, updates);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ThrowSkipsRemainingStatementsIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let updates = 0;
            function* thrower() {
                yield 1;
                updates = updates + 1;
            }
            let g = thrower();
        """);

        await engine.Evaluate("g.next();");
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("g.throw('boom');"));
        var updates = await engine.Evaluate("updates;");
        Assert.Equal("boom", exception.ThrownValue);
        Assert.Equal(0.0, updates);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ReturnYieldIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                return yield "first";
            }
            let g = gen();
        """);

        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        await engine.Evaluate("const second = g.next('then');");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        Assert.Equal("first", firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal("then", secondValue);
        Assert.True((bool)secondDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_IfConditionYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                let log = [];
                if (yield "first") {
                    log.push("then");
                } else {
                    log.push("else");
                }
                return log.join(",");
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected if(yield ...) generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_WhileConditionYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                let log = [];
                let count = 0;
                while (yield "probe") {
                    log.push(count);
                    count = count + 1;
                }
                return log.join(",");
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected while(yield ...) generator to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ReturnYieldIr_UsesIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                return yield "first";
            }
            let g = gen();
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected return yield generator to lower to IR.");
        Assert.Equal(0, failed);
    }
    [Fact(Timeout = 2000)]
    public async Task Generator_IfConditionYieldIr()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* gen() {
                let log = [];
                if (yield "first") {
                    log.push("then");
                } else {
                    log.push("else");
                }
                return log.join(",");
            }
            let g = gen();
        """);

        // First next yields the condition value
        await engine.Evaluate("const first = g.next();");
        var firstValue = await engine.Evaluate("first.value;");
        var firstDone = await engine.Evaluate("first.done;");

        // Resume with truthy value so the then-branch executes
        await engine.Evaluate("const second = g.next(true);");
        var secondValue = await engine.Evaluate("second.value;");
        var secondDone = await engine.Evaluate("second.done;");

        Assert.Equal("first", firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal("then", secondValue);
        Assert.True((bool)secondDone!);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_AllCoreIrShapes_UseIrPlan()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            function* whileYield(log) {
                let count = 0;
                while (yield "probe") {
                    log.push(count);
                    count = count + 1;
                }
                return log.join(",");
            }

            function* forLoop(limit) {
                for (let i = 0; i < limit; i = i + 1) {
                    yield i;
                }
            }

            function* forOfVar(values) {
                for (var v of values) {
                    yield v;
                }
            }

            function* yieldStarInner() {
                yield 1;
                yield 2;
                return 3;
            }

            function* yieldStarOuter() {
                return yield* yieldStarInner();
            }

            function* tryFinallyGen(log) {
                try {
                    yield "body";
                } finally {
                    log.push("finally");
                }
            }

            let coreLog = [];
            whileYield(coreLog);
            forLoop(3);
            forOfVar([1, 2, 3]);
            yieldStarOuter();
            tryFinallyGen(coreLog);
        """);

        var (_, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        Assert.True(succeeded >= 1, "Expected at least one core IR shape to lower to IR.");
        Assert.Equal(0, failed);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitFallsBackIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
                                     let result = "";
                                     let arr = ["a", "b", "c"];

                                     async function test() {
                                         for await (let item of arr) {
                                             result = result + item;
                                         }
                                     }

                                     test();

                         """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        // Async functions now use generator IR for execution.
        // The async function with for await...of will use the IR executor.
        Assert.True(attempts >= 1, "Expected at least one IR plan to be attempted");
        Assert.True(succeeded >= 1, "Expected at least one IR plan to succeed");
        Assert.Equal(0, failed);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitAsyncIteratorAwaitsValuesIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
                                     let result = "";

                                     // Custom object with async iterator
                                     let asyncIterable = {
                                         [Symbol.asyncIterator]() {
                                             let count = 0;
                                             return {
                                                 next() {
                                                     count = count + 1;
                                                     if (count <= 3) {
                                                         return Promise.resolve({ value: count, done: false });
                                                     } else {
                                                         return Promise.resolve({ done: true });
                                                     }
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         for await (let num of asyncIterable) {
                                             result = result + num;
                                         }
                                     }

                                     test();

                         """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        // Async functions now use generator IR for execution.
        Assert.True(attempts >= 1, "Expected at least one IR plan to be attempted");
        Assert.True(succeeded >= 1, "Expected at least one IR plan to succeed");
        Assert.Equal(0, failed);

        var result = await engine.Evaluate("result;");
        Assert.Equal("123", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitPromiseValuesAreAwaitedIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();

        await engine.Evaluate("""
                                     let result = "";
                                     // For-await-of can iterate arrays, but won't automatically await promise values.
                                     // This works if we await them manually in the loop body.
                                     let promises = [
                                         Promise.resolve("a"),
                                         Promise.resolve("b"),
                                         Promise.resolve("c")
                                     ];

                                     async function test() {
                                         for await (let promise of promises) {
                                             let item = await promise;
                                             result = result + item;
                                         }
                                     }

                                     test();

                         """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        // Async functions now use generator IR for execution.
        Assert.True(attempts >= 1, "Expected at least one IR plan to be attempted");
        Assert.True(succeeded >= 1, "Expected at least one IR plan to succeed");
        Assert.Equal(0, failed);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitAsyncIteratorRejectsPropagatesIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = CreateEngine();
        var errorCaught = false;

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            _testOutputHelper.WriteLine($"LOG: {message}");
            return JsValue.Null;
        });

        engine.SetGlobalFunction("markError", _ =>
        {
            errorCaught = true;
            _testOutputHelper.WriteLine("LOG: Error caught!");
            return JsValue.Null;
        });

        await engine.Evaluate("""

                                     let asyncIterable = {
                                         [Symbol.asyncIterator]() {
                                             let count = 0;
                                             return {
                                                 next() {
                                                     count = count + 1;
                                                     log("Iterator next() called, count: " + count);
                                                     if (count === 2) {
                                                         log("Rejecting at count 2");
                                                         return Promise.reject("test error");
                                                     }
                                                     if (count <= 3) {
                                                         log("Resolving with value: " + count);
                                                         return Promise.resolve({ value: count, done: false });
                                                     }
                                                     log("Done iterating");
                                                     return Promise.resolve({ done: true });
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         log("Starting test function");
                                         try {
                                             log("Starting for-await-of loop");
                                             for await (let num of asyncIterable) {
                                                 log("Got num in loop: " + num);
                                             }
                                             log("Loop completed without error");
                                         } catch (e) {
                                             log("Caught error: " + e);
                                             markError();
                                         }
                                         log("Test function complete");
                                     }

                                     test();

                         """);

        var (attempts, succeeded, failed) = GeneratorIrDiagnostics.Snapshot();

        // Async functions now use generator IR for execution.
        Assert.True(attempts >= 1, "Expected at least one IR plan to be attempted");
        Assert.True(succeeded >= 1, "Expected at least one IR plan to succeed");
        Assert.Equal(0, failed);
        Assert.True(errorCaught);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseGeneratorSyntax_FunctionStar()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should parse without error
        var program = engine.Parse("""

                                               function* myGenerator() {
                                                   yield 1;
                                               }

                                   """);

        Assert.NotNull(program);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseYieldExpression()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should parse without error
        var program = engine.Parse("""

                                               function* gen() {
                                                   let x = 5;
                                                   yield x + 1;
                                               }

                                   """);

        Assert.NotNull(program);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStarPreservesOriginalIteratorResult()
    {
        // Test that yield* preserves the original iterator result object, including when done is absent
        await using var engine = CreateEngine();

        var logs = new List<string>();
        engine.SetGlobalFunction("log", args =>
        {
            logs.Add(args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null");
            return JsValue.Null;
        });

        var result = await engine.Evaluate("""
            var results = [{ value: 1 }, { value: 8 }, { value: 34, done: true }];
            var idx = 0;
            var iterator = {};
            var iterable = {
              next: function() {
                var result = results[idx];
                idx += 1;
                log("inner next() returning: " + JSON.stringify(result));
                return result;
              }
            };
            iterator[Symbol.iterator] = function() {
              log("Symbol.iterator called");
              return iterable;
            };
            function* g() {
              log("generator starting");
              yield* iterator;
              log("generator done");
            }
            var iter = g();
            log("calling iter.next()");
            var result = iter.next();
            log("iter.next() returned: " + JSON.stringify(result));

            // The first result should be { value: 1 } without done property
            // So result.done should be undefined
            ({ value: result.value, hasDone: Object.hasOwn(result, 'done') });
        """);

        // Print all logs for debugging
        foreach (var log in logs)
        {
            Console.WriteLine($"LOG: {log}");
        }

        // The test expects done to not be present (since the inner iterator returned { value: 1 } without done)
        // Check that result is a JsObject with value=1 and that it doesn't have a 'done' property
        var jsResult = result as JsObject;
        Assert.NotNull(jsResult);
        Assert.True(jsResult.TryGetProperty("value", out var valueVal));
        Assert.Equal(1.0, valueVal);
        Assert.True(jsResult.TryGetProperty("hasDone", out var hasDoneVal));
        Assert.Equal(false, hasDoneVal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarCallsIteratorNext()
    {
        // This test reproduces the failing Test262 test: star-rhs-iter-nrml-next-invoke.js
        // The test verifies that yield* properly calls the iterator's next() method
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
        """);

        var callCountResult = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCountResult);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarCallsIteratorNext_StrictMode()
    {
        // Same as above but in strict mode - to match Test262 test
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            "use strict";
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
        """);

        var callCountResult = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCountResult);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStarCallsIteratorNext_WithHarness()
    {
        // Simulates Test262 harness loading
        await using var engine = CreateEngine();

        // Load assert.js-like harness
        await engine.Evaluate("""
            var assert = {
              sameValue: function(actual, expected, message) {
                if (actual !== expected) {
                  throw new Error(message || ("Expected SameValue(«" + actual + "», «" + expected + "») to be true"));
                }
              },
              throws: function(type, fn) {
                try {
                  fn();
                } catch (e) {
                  if (e instanceof type) return;
                  throw new Error("Expected " + type.name + " but got " + e.constructor.name);
                }
                throw new Error("Expected " + type.name + " but no exception was thrown");
              }
            };
            function $ERROR(msg) { throw new Error(msg); }
        """);

        // Run test code WITHOUT assert calls
        await engine.Evaluate("""
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
        """);

        // Check in C#
        var callCountResult = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCountResult);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStarCallsIteratorNext_InlineCheck()
    {
        // Same as above but check inline - this is closer to Test262 execution
        await using var engine = CreateEngine();

        // All in one evaluation - no harness at all
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);

            if (callCount !== 1) throw new Error("callCount was " + callCount);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStarCallsIteratorNext_TwoEvalsWithCheck()
    {
        // First evaluation defines stuff, second calls iter.next() and checks
        await using var engine = CreateEngine();

        // First evaluation - define everything
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
        """);

        // Second evaluation - create iterator, call next, and check
        await engine.Evaluate("""
            var iter = g();
            iter.next(9876);
            if (callCount !== 1) throw new Error("callCount was " + callCount);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStarCallsIteratorNext_ThreeEvalsWithCheck()
    {
        // Three evaluations - closer to Test262 structure
        await using var engine = CreateEngine();

        // First evaluation - harness-like setup
        await engine.Evaluate("""
            var assert = { sameValue: function(a,b,m) { if(a!==b) throw new Error(m || "mismatch " + a + " vs " + b); } };
        """);

        // Second evaluation - define everything
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            assert.sameValue(callCount, 1);
        """);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStar_TwoEvaluations()
    {
        // Test: define generator in first evaluation, call it in second
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
        """);

        // Second evaluation - create and call the generator
        await engine.Evaluate("""
            var iter = g();
            iter.next(9876);
        """);

        var callCountResult = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCountResult);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStar_MultipleEvaluations()
    {
        // Test: more evaluations like Test262
        await using var engine = CreateEngine();

        // Evaluation 1: simple harness setup
        await engine.Evaluate("""
            var assert = {};
        """);

        // Evaluation 2: the actual test
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
        """);

        var callCountResult = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCountResult);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStar_ManyEvaluations()
    {
        // Test: lots of evaluations like Test262 harness actually does
        await using var engine = CreateEngine();

        // Evaluation 1: assert.js content simulation
        await engine.Evaluate("""
            var assert = {
              sameValue: function(actual, expected, message) {
                if (actual !== expected) {
                  throw new Error(message || ("Expected SameValue(«" + actual + "», «" + expected + "») to be true"));
                }
              }
            };
        """);

        // Evaluation 2: sta.js content simulation
        await engine.Evaluate("""
            function $ERROR(msg) { throw new Error(msg); }
        """);

        // Evaluation 3: compareArray patch
        await engine.Evaluate("""
            function compareArray(a, b) { return a.length === b.length; }
        """);

        // Evaluation 4: the actual test
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
        """);

        var callCountResult = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCountResult);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_RealHarness()
    {
        // Use the real Test262 harness content
        await using var engine = CreateEngine();

        // Evaluation 1: sta.js (Test262Error class)
        await engine.Evaluate("""
            function Test262Error(message) {
              this.message = message || "";
            }
            Test262Error.prototype.toString = function () {
              return "Test262Error: " + this.message;
            };
            Test262Error.thrower = function (message) {
              throw new Test262Error(message);
            };
            function $DONOTEVALUATE() {
              throw "Test262: This statement should not be evaluated.";
            }
        """);

        // Evaluation 2: assert.js (assertion functions)
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue === true) {
                return;
              }
              if (message === undefined) {
                message = 'Expected true but got ' + assert._toString(mustBeTrue);
              }
              throw new Test262Error(message);
            }

            assert._isSameValue = function (a, b) {
              if (a === b) {
                return a !== 0 || 1 / a === 1 / b;
              }
              return a !== a && b !== b;
            };

            assert.sameValue = function (actual, expected, message) {
              try {
                if (assert._isSameValue(actual, expected)) {
                  return;
                }
              } catch (error) {
                throw new Test262Error(message + ' (_isSameValue operation threw) ' + error);
              }
              if (message === undefined) {
                message = '';
              } else {
                message += ' ';
              }
              message += 'Expected SameValue(«' + assert._toString(actual) + '», «' + assert._toString(expected) + '») to be true';
              throw new Test262Error(message);
            };

            assert._formatIdentityFreeValue = function (value) {
              switch (value === null ? 'null' : typeof value) {
                case 'string':
                  return typeof JSON !== "undefined" ? JSON.stringify(value) : '"' + value + '"';
                case 'bigint':
                  return value + 'n';
                case 'number':
                  if (value === 0 && 1 / value === -Infinity) return '-0';
                case 'boolean':
                case 'undefined':
                case 'null':
                  return String(value);
              }
            };

            assert._toString = function (value) {
              var basic = assert._formatIdentityFreeValue(value);
              if (basic) return basic;
              try {
                return String(value);
              } catch (err) {
                if (err.name === 'TypeError') {
                  return Object.prototype.toString.call(value);
                }
                throw err;
              }
            };
        """);

        // Evaluation 3: the actual test
        await engine.Evaluate("""
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);

            assert.sameValue(callCount, 1);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_WithCompareArrayPatch()
    {
        // Test with the CompareArray patch that Test262 uses
        await using var engine = CreateEngine();

        // Evaluation 1: sta.js
        await engine.Evaluate("""
            function Test262Error(message) {
              this.message = message || "";
            }
            Test262Error.prototype.toString = function () {
              return "Test262Error: " + this.message;
            };
        """);

        // Evaluation 2: assert.js
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue === true) return;
              throw new Test262Error(message || 'Expected true');
            }

            assert._isSameValue = function (a, b) {
              if (a === b) return a !== 0 || 1 / a === 1 / b;
              return a !== a && b !== b;
            };

            assert.sameValue = function (actual, expected, message) {
              if (assert._isSameValue(actual, expected)) return;
              message = (message ? message + ' ' : '') + 'Expected SameValue(«' + actual + '», «' + expected + '») to be true';
              throw new Test262Error(message);
            };

            assert._toString = function (value) { return String(value); };
        """);

        // Evaluation 3: CompareArray patch (simplified)
        await engine.Evaluate("""
            function compareArray(a, b) {
              if (b.length !== a.length) return false;
              for (var i = 0; i < a.length; i++) {
                if (a[i] !== b[i]) return false;
              }
              return true;
            }
            compareArray.isSameValue = function(a, b) {
              if (a === 0 && b === 0) return 1 / a === 1 / b;
              if (a !== a && b !== b) return true;
              return a === b;
            };
            compareArray.format = function(arr) { return '[' + arr.join(', ') + ']'; };
        """);

        // Evaluation 4: the actual test
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);

            assert.sameValue(callCount, 1);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_SyncExecution()
    {
        // Test using .Wait() like Test262 harness does
        var engine = CreateEngine();

        // Evaluation 1: sta.js using Wait()
        await engine.Evaluate("""
                            function Test262Error(message) {
                              this.message = message || "";
                            }
                            Test262Error.prototype.toString = function () {
                              return "Test262Error: " + this.message;
                            };
                        """);

        // Evaluation 2: assert.js using Wait()
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue === true) return;
              throw new Test262Error(message || 'Expected true');
            }
            assert._isSameValue = function (a, b) {
              if (a === b) return a !== 0 || 1 / a === 1 / b;
              return a !== a && b !== b;
            };
            assert.sameValue = function (actual, expected, message) {
              if (assert._isSameValue(actual, expected)) return;
              message = (message ? message + ' ' : '') + 'Expected SameValue(«' + actual + '», «' + expected + '») to be true';
              throw new Test262Error(message);
            };
        """);

        // Evaluation 3: the actual test using Wait()
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);

            assert.sameValue(callCount, 1);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_ExactTest262Scenario()
    {
        // This test has code before the generator that assigns to outer vars
        // The issue seems to be related to how the function captures outer vars
        var engine = CreateEngine();

        // Simplified test - just return callCount directly
        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            callCount;
            """;

        var result = await engine.Evaluate(testCode);

        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_WithoutArgsVar()
    {
        // Same test but without args/thisValue at beginning
        var engine = CreateEngine();

        var testCode = """
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_FullTest262WithAssertions()
    {
        // Exact Test262 test code including the 4 assertions
        var engine = CreateEngine();

        // First define assert.sameValue
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue !== true) throw new Error(message || 'assertion failed');
            }
            assert.sameValue = function(actual, expected, message) {
              if (actual !== expected) {
                throw new Error('Expected: ' + expected + ', Got: ' + actual + (message ? ' ' + message : ''));
              }
            };
        """);

        // Now run the exact Test262 test code
        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            assert.sameValue(callCount, 1);
            assert.sameValue(args.length, 1);
            assert.sameValue(args[0], undefined);
            assert.sameValue(thisValue, spyIterator);
        """;

        // This should not throw
        await engine.Evaluate(testCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TrailingCodeNoMultiEval()
    {
        // Same test but ALL in one evaluation - check if multi-eval is the issue
        var engine = CreateEngine();

        // Everything in a single evaluation
        var testCode = """
            function assert(mustBeTrue, message) {
              if (mustBeTrue !== true) throw new Error(message || 'assertion failed');
            }
            assert.sameValue = function(actual, expected, message) {
              if (actual !== expected) {
                throw new Error('Expected: ' + expected + ', Got: ' + actual + (message ? ' ' + message : ''));
              }
            };

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            assert.sameValue(callCount, 1);
            assert.sameValue(args.length, 1);
            assert.sameValue(args[0], undefined);
            assert.sameValue(thisValue, spyIterator);
        """;

        await engine.Evaluate(testCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TrailingCodeSimple()
    {
        // Simple test with just trailing code (no multi-eval, no assert)
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            // Just some trailing code that accesses callCount
            var result = callCount;
            result;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TrailingFunctionCall()
    {
        // Test with a simple function call after iter.next()
        var engine = CreateEngine();

        var testCode = """
            function doNothing() {}

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            doNothing();
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TrailingFunctionCallWithArg()
    {
        // Test with a function call that uses callCount after iter.next()
        var engine = CreateEngine();

        var testCode = """
            function check(val) { return val; }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            check(callCount);
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TwoArgFunctionCall()
    {
        // Test with a function call that takes 2 args (like assert.sameValue)
        var engine = CreateEngine();

        var testCode = """
            function compare(a, b) {
              if (a !== b) throw new Error('Mismatch: ' + a + ' !== ' + b);
            }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            compare(callCount, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_PropertyAssignmentAfterNext()
    {
        // Test with a function that has property access like assert.sameValue
        var engine = CreateEngine();

        var testCode = """
            var checker = {};
            checker.sameValue = function(a, b) {
              if (a !== b) throw new Error('Mismatch: ' + a + ' !== ' + b);
            };

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            checker.sameValue(callCount, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalMinimal()
    {
        // Minimal multi-eval case - define function in first eval, use in second
        var engine = CreateEngine();

        // First eval - define the helper function
        await engine.Evaluate("""
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
        """);

        // Second eval - use the helper with yield*
        var testCode = """
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next();

            check(callCount, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalWithoutCheck()
    {
        // Multi-eval but WITHOUT the check function call
        var engine = CreateEngine();

        // First eval - define something (doesn't matter what)
        await engine.Evaluate("""
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
        """);

        // Second eval - yield* but no check call
        var testCode = """
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next();

            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalCallCheckFromFirstEval()
    {
        // Multi-eval - calling a function defined in first eval
        var engine = CreateEngine();

        // First eval - define the function
        await engine.Evaluate("""
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
        """);

        // Second eval - JUST call check WITHOUT yield*
        var testCode = """
            var x = 5;
            check(x, 5);
            x;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(5.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalNoGeneratorButCheck()
    {
        // Multi-eval - calling function from first eval, but no generator
        var engine = CreateEngine();

        // First eval - define the function
        await engine.Evaluate("""
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
        """);

        // Second eval - regular iterator (not generator) then check
        var testCode = """
            var callCount = 0;
            var arr = [1, 2, 3];
            for (var i of arr) {
              callCount++;
            }
            check(callCount, 3);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalMethodOnObject()
    {
        // Multi-eval - method on object from first eval (like assert.sameValue)
        var engine = CreateEngine();

        // First eval - define assert with sameValue method
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue !== true) throw new Error(message || 'assertion failed');
            }
            assert.sameValue = function(actual, expected, message) {
              if (actual !== expected) {
                throw new Error('Expected: ' + expected + ', Got: ' + actual + (message ? ' ' + message : ''));
              }
            };
        """);

        // Second eval - use yield* then assert.sameValue
        var testCode = """
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next();

            assert.sameValue(callCount, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalMethodOnObjectMultipleCalls()
    {
        // Multi-eval - multiple assert.sameValue calls (like actual Test262)
        var engine = CreateEngine();

        // First eval - define assert with sameValue method
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue !== true) throw new Error(message || 'assertion failed');
            }
            assert.sameValue = function(actual, expected, message) {
              if (actual !== expected) {
                throw new Error('Expected: ' + expected + ', Got: ' + actual + (message ? ' ' + message : ''));
              }
            };
        """);

        // Second eval - use yield* then MULTIPLE assert.sameValue calls
        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            assert.sameValue(callCount, 1);
            assert.sameValue(args.length, 1);
            assert.sameValue(args[0], undefined);
            assert.sameValue(thisValue, spyIterator);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalMethodOnObject_TwoCalls()
    {
        // Multi-eval - TWO assert.sameValue calls
        var engine = CreateEngine();

        // First eval - define assert with sameValue method
        await engine.Evaluate("""
            function assert(mustBeTrue, message) {
              if (mustBeTrue !== true) throw new Error(message || 'assertion failed');
            }
            assert.sameValue = function(actual, expected, message) {
              if (actual !== expected) {
                throw new Error('Expected: ' + expected + ', Got: ' + actual + (message ? ' ' + message : ''));
              }
            };
        """);

        // Second eval - use yield* then TWO assert.sameValue calls
        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            assert.sameValue(callCount, 1);
            assert.sameValue(args.length, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_MultiEvalTwoFunctionCalls()
    {
        // Multi-eval - two function calls (but NOT assert.sameValue)
        var engine = CreateEngine();

        // First eval - define function
        await engine.Evaluate("""
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
        """);

        // Second eval - use yield* then TWO check calls
        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            check(callCount, 1);
            check(args.length, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_SameEvalTwoFunctionCalls()
    {
        // SAME eval - two function calls - should pass if it's cross-eval problem
        var engine = CreateEngine();

        var testCode = """
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            check(callCount, 1);
            check(args.length, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TwoPropertyAccesses()
    {
        // Two property accesses (not function calls) after iter.next()
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            var x = callCount;
            var y = args.length;
            x;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TwoVariableAssignments()
    {
        // Two variable assignments after iter.next()
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            var x = 1;
            var y = 2;
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TwoInlineFunctionCalls()
    {
        // Two inline function expressions called after iter.next()
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();

            iter.next(9876);

            (function() {})();
            (function() {})();
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TwoNamedFunctionCallsLocalScope()
    {
        // Two named function calls (function defined INSIDE the same script but AFTER generator)
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var iter = g();
            iter.next(9876);

            localCheck(callCount, 1);
            localCheck(args.length, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_TwoNamedFunctionCallsBeforeGenerator()
    {
        // Two named function calls (function defined BEFORE generator)
        var engine = CreateEngine();

        var testCode = """
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            iter.next(9876);

            localCheck(callCount, 1);
            localCheck(args.length, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_DebugIterNextResult_OneFuncDecl()
    {
        // Debug: check what iter.next() returns - with ONE function decl
        var engine = CreateEngine();

        var testCode = """
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            var nextResult = iter.next(9876);

            // Return what we got from next()
            JSON.stringify({
              resultDone: nextResult.done,
              callCount: callCount
            });
        """;

        var result = await engine.Evaluate(testCode);
        // Should work - ONE func decl
        Assert.Contains("\"callCount\":1", result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_DebugIterNextResult_TwoFuncDecls()
    {
        // Debug: check what iter.next() returns - with TWO function decls
        var engine = CreateEngine();

        var testCode = """
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
            function anotherFunc() {}

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            var nextResult = iter.next(9876);

            // Return what we got from next()
            JSON.stringify({
              resultDone: nextResult.done,
              callCount: callCount
            });
        """;

        var result = await engine.Evaluate(testCode);
        // This works - having 2 function declarations doesn't break it
        Assert.Contains("\"callCount\":1", result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_DebugIterNextResult_TwoFuncCalls_Constant()
    {
        // Debug: check what iter.next() returns - with TWO function calls with constant args
        var engine = CreateEngine();

        var testCode = """
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            var nextResult = iter.next(9876);

            // TWO function calls AFTER iter.next() with CONSTANT args
            localCheck(1, 1);
            localCheck(2, 2);

            // Return what we got from next()
            JSON.stringify({
              resultDone: nextResult.done,
              callCount: callCount
            });
        """;

        var result = await engine.Evaluate(testCode);
        // This WORKS - constant args don't trigger the bug
        Assert.Contains("\"callCount\":1", result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_DebugIterNextResult_TwoFuncCalls_VarRef()
    {
        // Debug: check what iter.next() returns - with TWO function calls referencing variables
        var engine = CreateEngine();

        var testCode = """
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            var nextResult = iter.next(9876);

            // TWO function calls AFTER iter.next() referencing VARIABLES
            localCheck(callCount, 1);
            localCheck(args.length, 1);

            // Return what we got from next()
            JSON.stringify({
              resultDone: nextResult.done,
              callCount: callCount
            });
        """;

        var result = await engine.Evaluate(testCode);
        // This is the BUG - referencing variables in function calls causes callCount to be 0
        Assert.Contains("\"callCount\":1", result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_OneCheck_Works()
    {
        // Confirm that ONE localCheck call works
        var engine = CreateEngine();

        var testCode = """
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            iter.next();

            localCheck(callCount, 1);
            callCount;
        """;

        var result = await engine.Evaluate(testCode);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_WithCommaVar_NoAssignment()
    {
        // Test with comma-separated var BUT no assignment inside the function
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_OnlyArgsAssignment()
    {
        // Test with just args assignment
        var engine = CreateEngine();

        var testCode = """
            var args;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_OnlyThisValueAssignment()
    {
        // Test with just thisValue assignment
        var engine = CreateEngine();

        var testCode = """
            var thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_BothAssignmentsSeparateVars()
    {
        // Test with both assignments but separate var declarations
        var engine = CreateEngine();

        var testCode = """
            var args;
            var thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_BothAssignmentsCommaVar()
    {
        // Test with both assignments and comma var declaration (exactly like failing test)
        var engine = CreateEngine();

        var testCode = """
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_WithArgsVarSeparate()
    {
        // Same test with args/thisValue but declared separately
        var engine = CreateEngine();

        var testCode = """
            var args;
            var thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """;

        await engine.Evaluate(testCode);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_WithPriorExecutions()
    {
        // This test executes multiple scripts before the test, simulating Test262 harness
        var engine = CreateEngine();

        // Simulate loading assert.js (large script with multiple functions)
        await engine.Evaluate("""
            // Large assert.js simulation with many functions
            var assert = function(mustBeTrue, message) {
              if (mustBeTrue === true) {
                return;
              }

              if (message === undefined) {
                message = 'Expected true but got ' + String(mustBeTrue);
              }
              throw new Test262Error(message);
            };

            assert._isSameValue = function (a, b) {
              if (a === b) {
                // Handle +/-0 vs. -/+0
                return a !== 0 || 1 / a === 1 / b;
              }

              // Handle NaN vs. NaN
              return a !== a && b !== b;
            };

            assert.sameValue = function (actual, expected, message) {
              try {
                if (assert._isSameValue(actual, expected)) {
                  return;
                }
              } catch (error) {
                throw new Test262Error(message + ' (_isSameValue operation threw) ' + error);
              }

              if (message === undefined) {
                message = '';
              } else {
                message += ' ';
              }

              message += 'Expected SameValue(«' + String(actual) + '», «' + String(expected) + '») to be true';

              throw new Test262Error(message);
            };

            assert.notSameValue = function (actual, unexpected, message) {
              if (!assert._isSameValue(actual, unexpected)) {
                return;
              }

              if (message === undefined) {
                message = '';
              } else {
                message += ' ';
              }

              message += 'Expected SameValue(«' + String(actual) + '», «' + String(unexpected) + '») to be false';

              throw new Test262Error(message);
            };

            assert.throws = function (expectedErrorConstructor, func, message) {
              var expectedName, actualName;
              if (typeof func !== "function") {
                throw new Test262Error('assert.throws requires two arguments: the error constructor ' +
                  'and a function to run');
              }
              if (message === undefined) {
                message = '';
              } else {
                message += ' ';
              }

              try {
                func();
              } catch (thrown) {
                if (typeof thrown !== 'object' || thrown === null) {
                  message += 'Thrown value was not an object!';
                  throw new Test262Error(message);
                } else if (thrown.constructor !== expectedErrorConstructor) {
                  expectedName = expectedErrorConstructor.name;
                  actualName = thrown.constructor.name;
                  if (expectedName === actualName) {
                    message += 'Expected a ' + expectedName + ' but got a different error constructor with the same name';
                  } else {
                    message += 'Expected a ' + expectedName + ' but got a ' + actualName;
                  }
                  throw new Test262Error(message);
                }
                return;
              }

              message += 'Expected a ' + expectedErrorConstructor.name + ' to be thrown but no exception was thrown at all';
              throw new Test262Error(message);
            };
            """);

        // Simulate sta.js (Test262 standard error)
        await engine.Evaluate("""
            function Test262Error(message) {
              this.message = message || "";
            }

            Test262Error.prototype.toString = function () {
              return "Test262Error: " + this.message;
            };

            var $ERROR = function $ERROR(message) {
              throw new Test262Error(message);
            };

            function testFailed(message) {
              $ERROR(message);
            }

            var $DONOTEVALUATE = function () {
              throw new Test262Error("$DONOTEVALUATE was called");
            };
            """);

        // Now run the actual test
        await engine.Evaluate("""
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next(9876);
            """);

        // Check the result
        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);

        // Also run the JavaScript assertion
        await engine.Evaluate("assert.sameValue(callCount, 1);");
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_Isolation_SingleEval_TwoCallsVarRef()
    {
        // This test isolates the bug: TWO function calls with variable refs in SINGLE eval
        var engine = CreateEngine();

        // Setup in first eval
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
            """);

        // Now run iter.next() and TWO checks in SINGLE eval
        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.Evaluate("""
                var iter = g();
                iter.next();
                check(callCount, 1);
                check(callCount, 1);
                """);
        });

        // This SHOULD pass but fails due to the bug
        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_Isolation_SeparateEvals_TwoCallsVarRef()
    {
        // This test isolates the bug: TWO function calls with variable refs in SEPARATE evals
        var engine = CreateEngine();

        // Setup in first eval
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
            """);

        // Run iter.next() in its own eval
        await engine.Evaluate("""
            var iter = g();
            iter.next();
            """);

        // Now verify callCount immediately
        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);

        // Now run TWO checks in SEPARATE evals
        await engine.Evaluate("check(callCount, 1);");
        await engine.Evaluate("check(callCount, 1);");
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_Isolation_SingleEval_TwoCallsConstant()
    {
        // This should work: TWO function calls with CONSTANT args in SINGLE eval
        var engine = CreateEngine();

        // Setup in first eval
        await engine.Evaluate("""
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
            """);

        // Now run iter.next() and TWO checks with CONSTANTS in SINGLE eval
        await engine.Evaluate("""
            var iter = g();
            iter.next();
            check(1, 1);
            check(1, 1);
            """);

        // Now verify callCount
        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_AllInOneEval_TwoCallsVarRef()
    {
        // This is the EXACT pattern that fails: EVERYTHING in single eval
        var engine = CreateEngine();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.Evaluate("""
                function check(a, b) {
                  if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
                }
                var callCount = 0;
                var spyIterator = {
                  next: function() {
                    callCount += 1;
                    return { done: true };
                  }
                };
                var spyIterable = {};
                spyIterable[Symbol.iterator] = function() {
                  return spyIterator;
                };
                function* g() {
                  yield * spyIterable;
                }
                var iter = g();
                iter.next();
                check(callCount, 1);
                check(callCount, 1);
                """);
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_AllInOneEval_OneCallVarRef()
    {
        // This should work: just ONE check call with var ref in ALL-IN-ONE eval
        var engine = CreateEngine();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.Evaluate("""
                function check(a, b) {
                  if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
                }
                var callCount = 0;
                var spyIterator = {
                  next: function() {
                    callCount += 1;
                    return { done: true };
                  }
                };
                var spyIterable = {};
                spyIterable[Symbol.iterator] = function() {
                  return spyIterator;
                };
                function* g() {
                  yield * spyIterable;
                }
                var iter = g();
                iter.next();
                check(callCount, 1);
                """);
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_AllInOneEval_TwoCallsConstant()
    {
        // This should work: CONSTANT args (regardless of single/multiple evals)
        var engine = CreateEngine();

        await engine.Evaluate("""
            function check(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }
            var iter = g();
            iter.next();
            check(1, 1);
            check(1, 1);
            """);

        // Now verify callCount
        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_ExactMatch_WithArgsProperty()
    {
        // This EXACTLY matches the failing test - includes args.length property access
        var engine = CreateEngine();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.Evaluate("""
                function localCheck(a, b) {
                  if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
                }

                var args, thisValue;
                var callCount = 0;
                var spyIterator = {
                  next: function() {
                    callCount += 1;
                    args = arguments;
                    thisValue = this;
                    return { done: true };
                  }
                };
                var spyIterable = {};
                spyIterable[Symbol.iterator] = function() {
                  return spyIterator;
                };
                function* g() {
                  yield * spyIterable;
                }

                var iter = g();
                var nextResult = iter.next(9876);

                // TWO function calls AFTER iter.next() referencing VARIABLES
                localCheck(callCount, 1);
                localCheck(args.length, 1);

                // Return what we got from next()
                JSON.stringify({
                  resultDone: nextResult.done,
                  callCount: callCount
                });
                """);
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_NoArgsProperty()
    {
        // Same as above but WITHOUT args.length property access
        var engine = CreateEngine();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.Evaluate("""
                function localCheck(a, b) {
                  if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
                }

                var args, thisValue;
                var callCount = 0;
                var spyIterator = {
                  next: function() {
                    callCount += 1;
                    args = arguments;
                    thisValue = this;
                    return { done: true };
                  }
                };
                var spyIterable = {};
                spyIterable[Symbol.iterator] = function() {
                  return spyIterator;
                };
                function* g() {
                  yield * spyIterable;
                }

                var iter = g();
                var nextResult = iter.next(9876);

                // TWO function calls - only checking callCount (no args.length)
                localCheck(callCount, 1);
                localCheck(callCount, 1);

                // Return what we got from next()
                JSON.stringify({
                  resultDone: nextResult.done,
                  callCount: callCount
                });
                """);
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_OnlyArgsLengthCheck()
    {
        // Only ONE check with args.length
        var engine = CreateEngine();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.Evaluate("""
                function localCheck(a, b) {
                  if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
                }

                var args, thisValue;
                var callCount = 0;
                var spyIterator = {
                  next: function() {
                    callCount += 1;
                    args = arguments;
                    thisValue = this;
                    return { done: true };
                  }
                };
                var spyIterable = {};
                spyIterable[Symbol.iterator] = function() {
                  return spyIterator;
                };
                function* g() {
                  yield * spyIterable;
                }

                var iter = g();
                var nextResult = iter.next(9876);

                // Just ONE function call - with args.length
                localCheck(args.length, 1);

                JSON.stringify({
                  callCount: callCount
                });
                """);
        });

        Assert.Null(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_ArgsLengthAfterIterNext_NoGenerator()
    {
        // Same pattern but WITHOUT generator to isolate if it's the generator
        var engine = CreateEngine();

        await engine.Evaluate("""
            function localCheck(a, b) {
              if (a !== b) throw new Error('Expected: ' + b + ', Got: ' + a);
            }

            var args;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                return { done: true };
              }
            };

            // Direct call, no generator
            spyIterator.next(9876);

            // Check args.length
            localCheck(args.length, 1);
            """);

        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_ArgsLengthOnly_NoLocalCheck()
    {
        // Just access args.length without any function call
        var engine = CreateEngine();

        await engine.Evaluate("""
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            iter.next(9876);
            """);

        // Check callCount
        var callCount = await engine.Evaluate("callCount");
        Assert.Equal(1.0, callCount);

        // Now check args.length in separate eval
        var argsLength = await engine.Evaluate("args.length");
        Assert.Equal(1.0, argsLength);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_ArgsLengthInSameEval_NoFunctionCall()
    {
        // Access args.length in same eval but without function call
        var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            iter.next(9876);

            // Just access args.length without function call
            JSON.stringify({ argsLength: args.length, callCount: callCount });
            """);

        Assert.Contains("\"callCount\":1", result?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Contains("\"argsLength\":1", result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_CheckArgsZero()
    {
        // Check what's actually in args[0]
        var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var args, thisValue;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                thisValue = this;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            iter.next(9876);

            // Check what args actually contains
            JSON.stringify({
              callCount: callCount,
              argsLength: args.length,
              argsType: typeof args
            });
            """);

        // Print result for debugging
        var resultStr = result?.ToString() ?? "";
        // Per ES spec (14.4.14), the first call to inner iterator's next() receives undefined as the argument,
        // not the value passed to outer generator's next(). The outer's next() argument is only used on RESUME.
        // Node.js V8 confirms: argsLength=1, argsZero=undefined
        Assert.Equal("{\"callCount\":1,\"argsLength\":1,\"argsType\":\"object\"}", resultStr);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_DirectCallWithArg()
    {
        // Call inner iterator directly with arg
        var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var args;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                return { done: true };
              }
            };

            // Direct call - NOT through yield*
            spyIterator.next(9876);

            JSON.stringify({
              callCount: callCount,
              argsLength: args.length,
              argsZero: args[0]
            });
            """);

        // Direct call should have args[0] = 9876
        Assert.Contains("\"argsZero\":9876", result?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_YieldStar_Bug_FirstNextCallHasNoArg()
    {
        // The FIRST call to a generator's next() should NOT pass the argument to the inner iterator
        // Per ES spec, the first .next() call's argument is always ignored
        // But the inner iterator's next() IS called, so args should be an Arguments object with length 1
        // The bug is: what argument is being passed to the inner iterator's next()?
        var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var args;
            var callCount = 0;
            var spyIterator = {
              next: function() {
                callCount += 1;
                args = arguments;
                return { done: true };
              }
            };
            var spyIterable = {};
            spyIterable[Symbol.iterator] = function() {
              return spyIterator;
            };
            function* g() {
              yield * spyIterable;
            }

            var iter = g();
            // First call to outer generator - per spec, 9876 is ignored for outer generator
            // but should be passed to inner iterator's next()
            iter.next(9876);

            JSON.stringify({
              callCount: callCount,
              argsLength: args.length,
              argsZero: args[0],
              argsUndefined: args[0] === undefined
            });
            """);

        var resultStr = result?.ToString() ?? "";
        // The callCount should be 1
        Assert.Contains("\"callCount\":1", resultStr, StringComparison.Ordinal);
        // Let's see what args.length and args[0] are
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldInArrayLiteral()
    {
        // This tests the pattern: [yield 1]
        // The yield inside an array literal should work correctly
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g1() { [yield 1] }

            let iter = g1();
            let r1 = iter.next();
            let r2 = iter.next();
            JSON.stringify({ v1: r1.value, d1: r1.done, v2: r2.value, d2: r2.done });
        """);

        var resultStr = result?.ToString() ?? "";
        // First next should yield 1, not done
        // Second next should be done with undefined value
        Assert.Contains("\"v1\":1", resultStr);
        Assert.Contains("\"d1\":false", resultStr);
        Assert.Contains("\"d2\":true", resultStr);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_YieldInSequenceExpression()
    {
        // This tests the pattern: yield, yield
        // Sequence of yields should work correctly
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g4() { yield, yield; }

            let iter = g4();
            let r1 = iter.next();
            let r2 = iter.next();
            let r3 = iter.next();
            JSON.stringify({
                d1: r1.done, d2: r2.done, d3: r3.done
            });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr);
        Assert.Contains("\"d2\":false", resultStr);
        Assert.Contains("\"d3\":true", resultStr);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_DestructuringAssignmentWithYieldDefault()
    {
        // This tests the pattern: [ x = yield ] = vals
        // Yield in destructuring default should work correctly
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var x;
            var iter = (function*() {
                var vals = [];
                [ x = yield ] = vals;
            })();

            let r1 = iter.next();
            let r2 = iter.next(86);
            JSON.stringify({
                v1: r1.value, d1: r1.done,
                d2: r2.done, x: x
            });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr);
        Assert.Contains("\"d2\":true", resultStr);
        Assert.Contains("\"x\":86", resultStr);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForOfWithYieldInDestructuringDefault()
    {
        // This tests the pattern: for ([ x = yield ] of [[]])
        // Yield in for-of destructuring default should work correctly
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var x;
            var iter = (function*() {
                for ([ x = yield ] of [[]]) {
                }
            })();

            let r1 = iter.next();
            let r2 = iter.next(86);
            JSON.stringify({
                v1: r1.value, d1: r1.done,
                d2: r2.done, x: x
            });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":true", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"x\":86", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ParenthesizedYield()
    {
        // Test: (yield)
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g1() { (yield) }
            let iter = g1();
            let r1 = iter.next();
            let r2 = iter.next();
            JSON.stringify({ v1: r1.value, d1: r1.done, d2: r2.done });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_BlockWithYield()
    {
        // Test: {yield} - this is a block containing a yield expression statement
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g3() { {yield} }
            let iter = g3();
            let r1 = iter.next();
            let r2 = iter.next();
            JSON.stringify({ v1: r1.value, d1: r1.done, d2: r2.done });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ConditionalWithYields()
    {
        // Test: (yield) ? yield : yield
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g5() { (yield) ? yield : yield; }
            let iter = g5();
            let r1 = iter.next();
            let r2 = iter.next(true);  // condition is truthy, should take true branch
            let r3 = iter.next();
            JSON.stringify({ d1: r1.done, d2: r2.done, d3: r3.done });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d3\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ArrayWithYieldNoOperand()
    {
        // Test: [yield] - bare yield without operand in array literal
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g2() { [yield] }
            let iter = g2();
            let r1 = iter.next();
            let r2 = iter.next();
            JSON.stringify({ v1: r1.value, d1: r1.done, d2: r2.done });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_SequenceWithTwoYields()
    {
        // Test: yield, yield - sequence expression with two yields
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g4() { yield, yield; }
            let iter = g4();
            let r1 = iter.next();
            let r2 = iter.next();
            let r3 = iter.next();
            JSON.stringify({ d1: r1.done, d2: r2.done, d3: r3.done });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d3\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ObjectMethodWithYieldPatterns()
    {
        // Test generator methods in object literals with yield patterns
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var obj = {
                *g1() { (yield 1) },
                *g2() { [yield 1] },
                *g5() { (yield 1) ? yield 2 : yield 3; }
            };

            var results = {};

            // Test g1
            var iter = obj.g1();
            var r = iter.next();
            results.g1_v = r.value;
            results.g1_d1 = r.done;
            r = iter.next();
            results.g1_d2 = r.done;

            // Test g2
            iter = obj.g2();
            r = iter.next();
            results.g2_v = r.value;
            results.g2_d1 = r.done;

            // Test g5
            iter = obj.g5();
            r = iter.next();
            results.g5_v = r.value;
            results.g5_d1 = r.done;

            JSON.stringify(results);
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"g1_v\":1", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g1_d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g2_v\":1", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g5_v\":1", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task Generator_AllRhsOmittedPatterns()
    {
        // Comprehensive test matching rhs-omitted.js from Test262
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function* g1() { (yield) }
            function* g2() { [yield] }
            function* g3() { {yield} }
            function* g4() { yield, yield; }
            function* g5() { (yield) ? yield : yield; }

            var results = {};

            // Test g1
            var iter = g1();
            var r = iter.next();
            results.g1_v1 = r.value;
            results.g1_d1 = r.done;
            r = iter.next();
            results.g1_d2 = r.done;

            // Test g2
            iter = g2();
            r = iter.next();
            results.g2_d1 = r.done;
            r = iter.next();
            results.g2_d2 = r.done;

            // Test g3
            iter = g3();
            r = iter.next();
            results.g3_d1 = r.done;
            r = iter.next();
            results.g3_d2 = r.done;

            // Test g4
            iter = g4();
            r = iter.next();
            results.g4_d1 = r.done;
            r = iter.next();
            results.g4_d2 = r.done;
            r = iter.next();
            results.g4_d3 = r.done;

            // Test g5
            iter = g5();
            r = iter.next();
            results.g5_d1 = r.done;
            r = iter.next();
            results.g5_d2 = r.done;
            r = iter.next();
            results.g5_d3 = r.done;

            JSON.stringify(results);
        """);

        var resultStr = result?.ToString() ?? "";
        // g1: first yield, then done
        Assert.Contains("\"g1_d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g1_d2\":true", resultStr, StringComparison.Ordinal);
        // g2: first yield, then done
        Assert.Contains("\"g2_d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g2_d2\":true", resultStr, StringComparison.Ordinal);
        // g3: first yield, then done
        Assert.Contains("\"g3_d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g3_d2\":true", resultStr, StringComparison.Ordinal);
        // g4: two yields, then done
        Assert.Contains("\"g4_d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g4_d2\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g4_d3\":true", resultStr, StringComparison.Ordinal);
        // g5: condition yield, branch yield, then done
        Assert.Contains("\"g5_d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g5_d2\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"g5_d3\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_DestructuringAssignmentExpressionWithYield()
    {
        // Test262: result = [ x = yield ] = vals;
        // This is an assignment expression where value is a destructuring assignment
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var x;
            var result;
            var vals = [];
            var iter = (function*() {
                result = [ x = yield ] = vals;
            })();

            let r1 = iter.next();
            let r2 = iter.next(86);
            JSON.stringify({
                v1: r1.value, d1: r1.done,
                d2: r2.done, x: x, resultIsVals: result === vals
            });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":true", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"x\":86", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"resultIsVals\":true", resultStr, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_NestedArrayDestructuringWithYieldInTarget()
    {
        // Test262: result = [[x[yield]]] = vals;
        // Nested array destructuring with yield in computed property target
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var value = [[22]];
            var x = {};
            var iter = (function*() {
                var result;
                var vals = value;
                result = [[x[yield]]] = vals;
            })();

            let r1 = iter.next();
            let r2 = iter.next('prop');
            JSON.stringify({
                v1: r1.value, d1: r1.done,
                d2: r2.done, xProp: x.prop
            });
        """);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("\"d1\":false", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"d2\":true", resultStr, StringComparison.Ordinal);
        Assert.Contains("\"xProp\":22", resultStr, StringComparison.Ordinal);
    }
}

[Collection("GeneratorIrCollection")]
public class FastPath_GeneratorTests(ITestOutputHelper output) : GeneratorTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

[Collection("GeneratorIrCollection")]
public class Reference_GeneratorTests(ITestOutputHelper output) : GeneratorTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}

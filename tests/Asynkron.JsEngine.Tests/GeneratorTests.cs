using Asynkron.JsEngine;
using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for generator functions (function*) and the iterator protocol.
/// </summary>
[Collection("GeneratorIrCollection")]
public class GeneratorTests
{
    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.

    [Fact(Timeout = 2000)]
    public async Task GeneratorFunction_CanBeDeclared()
    {
        // Arrange
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
    public async Task Generator_YieldStar_DelegatesValues()
    {
        // Arrange
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        if (signal.ThrownValue is Asynkron.JsEngine.JsTypes.JsObject obj &&
            obj.TryGetProperty("message", out var message) &&
            message is string msg)
        {
            Assert.Equal("Iterator result is not an object.", msg);
            return;
        }

        if (signal.ThrownValue is string str)
        {
            Assert.Equal("Iterator result is not an object.", str);
            return;
        }

        Assert.Fail($"Unexpected thrown value: {signal.ThrownValue}");
    }


    [Fact(Timeout = 2000)]
    public async Task Generator_YieldStarThrowAwaitedPromiseIr()
    {
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

        await engine.Run("""
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

        // for await...of does not use generator IR; it stays on the
        // replay/async path, so no IR plans should have been attempted.
        Assert.Equal(0, attempts);
        Assert.Equal(0, succeeded);
        Assert.Equal(0, failed);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitAsyncIteratorAwaitsValuesIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = new JsEngine();

        await engine.Run("""
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

        // Async iteration executes without engaging generator IR.
        Assert.Equal(0, attempts);
        Assert.Equal(0, succeeded);
        Assert.Equal(0, failed);

        var result = await engine.Evaluate("result;");
        Assert.Equal("123", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitPromiseValuesAreAwaitedIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = new JsEngine();

        await engine.Run("""
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

        Assert.Equal(0, attempts);
        Assert.Equal(0, succeeded);
        Assert.Equal(0, failed);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Generator_ForAwaitAsyncIteratorRejectsPropagatesIr()
    {
        GeneratorIrDiagnostics.Reset();
        await using var engine = new JsEngine();
        var errorCaught = false;

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            Console.WriteLine($"LOG: {message}");
            return null;
        });

        engine.SetGlobalFunction("markError", args =>
        {
            errorCaught = true;
            Console.WriteLine("LOG: Error caught!");
            return null;
        });

        await engine.Run("""

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

        Assert.Equal(0, attempts);
        Assert.Equal(0, succeeded);
        Assert.Equal(0, failed);
        Assert.True(errorCaught);
    }

    [Fact(Timeout = 2000)]
    public async Task ParseGeneratorSyntax_FunctionStar()
    {
        // Arrange
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

        // Act & Assert - Should parse without error
        var program = engine.Parse("""

                                               function* gen() {
                                                   let x = 5;
                                                   yield x + 1;
                                               }

                                   """);

        Assert.NotNull(program);
    }
}

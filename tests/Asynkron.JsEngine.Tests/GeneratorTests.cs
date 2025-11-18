using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for generator functions (function*) and the iterator protocol.
/// </summary>
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
        var value = await engine.Evaluate("finalResult.value;");

        // Assert
        Assert.True((bool)done!);
        Assert.Null(value);
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
        var thirdValue = await engine.Evaluate("r3.value;");
        var firstDone = await engine.Evaluate("r1.done;");
        var secondDone = await engine.Evaluate("r2.done;");
        var thirdDone = await engine.Evaluate("r3.done;");

        // Assert
        Assert.Equal(10.0, firstValue);
        Assert.False((bool)firstDone!);
        Assert.Equal(5.0, secondValue);
        Assert.False((bool)secondDone!);
        Assert.Null(thirdValue);
        Assert.True((bool)thirdDone!);
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
    public async Task Generator_TryFinallyNestedReturnIr()
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

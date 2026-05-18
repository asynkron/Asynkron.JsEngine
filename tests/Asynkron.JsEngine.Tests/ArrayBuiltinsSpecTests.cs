using System.Linq;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibArray)]
public sealed class ArrayBuiltinsSpecTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Array_toLocaleString_InvokesElementMethodWithArgs()
    {
        await using var engine = CreateEngine();

        var result = Assert.IsType<JsObject>(await engine.Evaluate("""
            var callCount = 0;
            var lastArgs;
            const element = {
                toLocaleString(...args) {
                    callCount++;
                    lastArgs = args;
                    return "ok";
                }
            };
            const output = [element].toLocaleString("th-u-nu-thai", { minimumFractionDigits: 3 });
            ({ output, callCount, arg0: lastArgs[0], arg1: lastArgs[1] });
        """));

        Assert.Equal("ok", result["output"]);
        Assert.Equal(1d, result["callCount"]);
        Assert.Equal("th-u-nu-thai", result["arg0"]);
        Assert.IsType<JsObject>(result["arg1"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_indexOf_ObservesPropertiesAddedDuringIteration()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var arr = {
              length: 2
            };

            Object.defineProperty(arr, "0", {
              get: function() {
                Object.defineProperty(arr, "1", {
                  get: function() {
                    return 1;
                  },
                  configurable: true
                });
                return 0;
              },
              configurable: true
            });

            Array.prototype.indexOf.call(arr, 1);
        """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_indexOf_TreatsNegativeZeroFromIndexAsPositiveZero()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("Object.is([true].indexOf(true, -0), 0);");

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact(Timeout = 2000)]
    public async Task Array_indexOfAndLastIndexOf_TreatSignedZeroAsStrictlyEqual()
    {
        await using var engine = CreateEngine();

        Assert.False(Assert.IsType<bool>(await engine.Evaluate("Object.is(-0, 0);")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("Object.is(NaN, NaN);")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("Object.is([-0].indexOf(+0), 0);")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("Object.is([-0].lastIndexOf(+0), 0);")));
        Assert.Equal(-1d, await engine.Evaluate("[NaN].indexOf(NaN);"));
        Assert.Equal(-1d, await engine.Evaluate("[NaN].lastIndexOf(NaN);"));
    }

    [Fact(Timeout = 2000)]
    public async Task Array_at_SymbolIndexThrowsTypeError()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            "use strict";
            var a = [0, 1, 2, 3];
            var outcome = { kind: "unset" };
            try {
              a.at(Symbol());
              outcome = { kind: "no-throw" };
            } catch (err) {
              outcome = {
                kind: "throw",
                type: typeof err,
                ctor: err && err.constructor && err.constructor.name,
                name: err && err.name,
                message: err && err.message
              };
            }
            outcome;
        """);

        var record = Assert.IsType<JsObject>(result);
        var kind = record["kind"];
        var type = record["type"];
        var ctor = record["ctor"];
        var name = record["name"];
        var message = record["message"];

        Assert.Equal("throw", kind);
        Assert.Equal("object", type);
        Assert.Equal("TypeError", ctor);
        Assert.Equal("TypeError", name);
        Assert.NotNull(message);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_filter_ThrowsTypeError_WhenResultIsNonExtensible()
    {
        // Test262: target-array-non-extensible.js
        // When Symbol.species returns a non-extensible constructor,
        // filter should throw TypeError when trying to add properties
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var A = function(_length) {
              this.length = 0;
              Object.preventExtensions(this);
            };

            var arr = [1];
            arr.constructor = {};
            arr.constructor[Symbol.species] = A;

            var threw = 'no error';
            try {
              arr.filter(function() {
                return true;
              });
            } catch (e) {
              threw = e instanceof TypeError ? 'TypeError' : ('other: ' + e);
            }
            threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_map_ThrowsTypeError_WhenResultIsNonExtensible()
    {
        // Test262: target-array-non-extensible.js
        // When Symbol.species returns a non-extensible constructor,
        // map should throw TypeError when trying to add properties
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var A = function(_length) {
              this.length = 0;
              Object.preventExtensions(this);
            };

            var arr = [1];
            arr.constructor = {};
            arr.constructor[Symbol.species] = A;

            var threw = 'no error';
            try {
              arr.map(function(x) {
                return x;
              });
            } catch (e) {
              threw = e instanceof TypeError ? 'TypeError' : ('other: ' + e);
            }
            threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_flat_ThrowsTypeError_WhenResultIsNonExtensible()
    {
        // Test262: target-array-non-extensible.js
        // When Symbol.species returns a non-extensible constructor,
        // flat should throw TypeError when trying to add properties
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var A = function(_length) {
              this.length = 0;
              Object.preventExtensions(this);
            };

            var arr = [[1]];
            arr.constructor = {};
            arr.constructor[Symbol.species] = A;

            var threw = 'no error';
            try {
              arr.flat();
            } catch (e) {
              threw = e instanceof TypeError ? 'TypeError' : ('other: ' + e);
            }
            threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_flatMap_ThrowsTypeError_WhenResultIsNonExtensible()
    {
        // Test262: target-array-non-extensible.js
        // When Symbol.species returns a non-extensible constructor,
        // flatMap should throw TypeError when trying to add properties
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var A = function(_length) {
              this.length = 0;
              Object.preventExtensions(this);
            };

            var arr = [1];
            arr.constructor = {};
            arr.constructor[Symbol.species] = A;

            var threw = 'no error';
            try {
              arr.flatMap(function(x) {
                return x;
              });
            } catch (e) {
              threw = e instanceof TypeError ? 'TypeError' : ('other: ' + e);
            }
            threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_splice_ThrowsTypeError_WhenResultIsNonExtensible()
    {
        // Test262: target-array-non-extensible.js
        // When Symbol.species returns a non-extensible constructor,
        // splice should throw TypeError when trying to add properties
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var A = function(_length) {
              this.length = 0;
              Object.preventExtensions(this);
            };

            var arr = [1];
            arr.constructor = {};
            arr.constructor[Symbol.species] = A;

            var threw = 'no error';
            try {
              arr.splice(0);
            } catch (e) {
              threw = e instanceof TypeError ? 'TypeError' : ('other: ' + e);
            }
            threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_slice_ThrowsTypeError_WhenResultIsNonExtensible()
    {
        // Test262: target-array-non-extensible.js
        // When Symbol.species returns a non-extensible constructor,
        // slice should throw TypeError when trying to add properties
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var A = function(_length) {
              this.length = 0;
              Object.preventExtensions(this);
            };

            var arr = [1];
            arr.constructor = {};
            arr.constructor[Symbol.species] = A;

            var threw = 'no error';
            try {
              arr.slice(0, 1);
            } catch (e) {
              threw = e instanceof TypeError ? 'TypeError' : ('other: ' + e);
            }
            threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_length_valueOf_Object()
    {
        // Test262: S15.4.5.1_A1.3_T2.js
        // When setting array length to an object with valueOf, the valueOf should be called
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var x = [];
            x.length = {
              valueOf: function() {
                return 2
              }
            };
            x.length;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_length_valueOf_TakesPrecedenceOverToString()
    {
        // Test262: S15.4.5.1_A1.3_T2.js
        // valueOf should be used over toString when both are present
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var x = [];
            x.length = {
              valueOf: function() {
                return 2
              },
              toString: function() {
                return 1
              }
            };
            x.length;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_length_valueOf_ToStringNotCalledWhenValueOfReturnsPrimitive()
    {
        // Test262: S15.4.5.1_A1.3_T2.js
        // When valueOf returns a primitive, toString should NOT be called (even if it throws)
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var threw = false;
            try {
              var x = [];
              x.length = {
                valueOf: function() {
                  return 2
                },
                toString: function() {
                  throw "error"
                }
              };
            } catch (e) {
              threw = true;
            }
            // valueOf returned 2, so toString should NOT have been called
            !threw;
        """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_length_valueOf_Throws_ErrorPropagates()
    {
        // Test262: S15.4.5.1_A1.3_T2.js - case #7
        // When valueOf throws, the error should propagate and be catchable
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var caughtValue = "not caught";
            var caughtType = "unknown";
            try {
              var x = [];
              x.length = {
                valueOf: function() {
                  throw "error";
                },
                toString: function() {
                  return 1;
                }
              };
              caughtValue = "no throw";
            } catch (e) {
              caughtValue = e;
              caughtType = typeof e;
            }
            ({ caughtValue: caughtValue, caughtType: caughtType, isError: caughtValue === "error" });
        """);

        var record = Assert.IsType<JsObject>(result);
        Output.WriteLine($"caughtValue: {record["caughtValue"]}");
        Output.WriteLine($"caughtType: {record["caughtType"]}");
        Output.WriteLine($"isError: {record["isError"]}");

        Assert.Equal("error", record["caughtValue"]);
        Assert.Equal("string", record["caughtType"]);
        Assert.Equal(true, record["isError"]);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_length_valueOf_Full_Test262_Code()
    {
        // Full Test262: S15.4.5.1_A1.3_T2.js test code
        await using var engine = CreateEngine();

        // Define assert.sameValue for Test262 compatibility
        await engine.Evaluate("""
            function Test262Error(message) {
              this.message = message || "";
            }
            Test262Error.prototype.toString = function () {
              return "Test262Error: " + this.message;
            };

            var assert = {};
            assert._isSameValue = function (a, b) {
              if (a === b) {
                return a !== 0 || 1 / a === 1 / b;
              }
              return a !== a && b !== b;
            };
            assert.sameValue = function (actual, expected, message) {
              if (assert._isSameValue(actual, expected)) {
                return;
              }
              message = message || '';
              message += ' Expected: ' + String(expected) + ', Actual: ' + String(actual);
              throw new Test262Error(message);
            };
            assert.notSameValue = function (actual, unexpected, message) {
              if (!assert._isSameValue(actual, unexpected)) {
                return;
              }
              throw new Test262Error(message);
            };
        """);

        try
        {
            var result = await engine.Evaluate("""
                var x = [];
                x.length = {
                  valueOf: function() {
                    return 2
                  }
                };
                assert.sameValue(x.length, 2, 'The value of x.length is expected to be 2');

                x = [];
                x.length = {
                  valueOf: function() {
                    return 2
                  },
                  toString: function() {
                    return 1
                  }
                };
                assert.sameValue(x.length, 2, 'The value of x.length is expected to be 2');

                x = [];
                x.length = {
                  valueOf: function() {
                    return 2
                  },
                  toString: function() {
                    return {}
                  }
                };
                assert.sameValue(x.length, 2, 'The value of x.length is expected to be 2');

                try {
                  x = [];
                  x.length = {
                    valueOf: function() {
                      return 2
                    },
                    toString: function() {
                      throw "error"
                    }
                  };
                  assert.sameValue(x.length, 2, 'The value of x.length is expected to be 2');
                }
                catch (e) {
                  assert.notSameValue(e, "error", 'The value of e is not "error"');
                }

                x = [];
                x.length = {
                  toString: function() {
                    return 1
                  }
                };
                assert.sameValue(x.length, 1, 'The value of x.length is expected to be 1');

                x = [];
                x.length = {
                  valueOf: function() {
                    return {}
                  },
                  toString: function() {
                    return 1
                  }
                }
                assert.sameValue(x.length, 1, 'The value of x.length is expected to be 1');

                try {
                  x = [];
                  x.length = {
                    valueOf: function() {
                      throw "error"
                    },
                    toString: function() {
                      return 1
                    }
                  };
                  x.length;
                  throw new Test262Error('#7.1: x = []; x.length = {valueOf: function() {throw "error"}, toString: function() {return 1}}; x.length throw "error". Actual: ' + (x.length));
                }
                catch (e) {
                  assert.sameValue(e, "error", 'The value of e is expected to be "error"');
                }

                try {
                  x = [];
                  x.length = {
                    valueOf: function() {
                      return {}
                    },
                    toString: function() {
                      return {}
                    }
                  };
                  x.length;
                  throw new Test262Error('#8.1: x = []; x.length = {valueOf: function() {return {}}, toString: function() {return {}}}  x.length throw TypeError. Actual: ' + (x.length));
                }
                catch (e) {
                  assert.sameValue(
                    e instanceof TypeError,
                    true,
                    'The result of evaluating (e instanceof TypeError) is expected to be true'
                  );
                }

                "PASS"
            """);

            Output.WriteLine($"Result: {result}");
            Assert.Equal("PASS", result);
        }
        catch (ThrowSignal ex)
        {
            Output.WriteLine($"ThrowSignal: {ex.Message}");
            Output.WriteLine($"ThrownValue Kind: {ex.ThrownValue.Kind}");
            Output.WriteLine($"ThrownValue: {ex.ThrownValue.ToObject()}");
            if (ex.ThrownValue.TryGetObject<JsObject>(out var obj))
            {
                Output.WriteLine($"ThrownValue.message: {obj["message"]}");
                Output.WriteLine($"ThrownValue.name: {obj["name"]}");
            }
            throw;
        }
    }

    [Fact(Timeout = 5000)]
    public async Task Array_length_Shrink_Deletes_Accessor_Properties()
    {
        // Test262: built-ins/Array/prototype/reverse/get_if_present_with_delete.js
        await using var engine = CreateEngine();

        var result = Assert.IsType<JsObject>(await engine.Evaluate("""
            var array = ["first", "second"];

            Object.defineProperty(array, 0, {
              get: function() {
                array.length = 0;
                return "first";
              }
            });

            array.reverse();

            ({
              has0: 0 in array,
              has1: 1 in array,
              value1: array[1],
              length: array.length
            });
        """));

        Assert.Equal(false, result["has0"]);
        Assert.Equal(true, result["has1"]);
        Assert.Equal("first", result["value1"]);
        Assert.Equal(2d, result["length"]);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_flatMap_CallsCustomSpeciesConstructorWithNewTarget()
    {
        // Test for flatMap calling custom species constructor with new.target
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var arr = [];
            var mapperFn = function(e) { return e; };

            var called = 0;
            var ctorCalled = 0;
            var newTargetCorrect = false;
            function ctor(len) {
              newTargetCorrect = new.target === ctor;
              ctorCalled++;
              throw new Error('CustomError');
            }

            arr.constructor = {
              get [Symbol.species]() {
                called++;
                return ctor;
              }
            };
            var threwError = false;
            try {
              arr.flatMap(mapperFn);
            } catch (e) {
              threwError = e.message === 'CustomError';
            }
            ({ called, ctorCalled, threwError, newTargetCorrect });
        """);

        var record = Assert.IsType<JsObject>(result);
        Output.WriteLine($"called: {record["called"]}");
        Output.WriteLine($"ctorCalled: {record["ctorCalled"]}");
        Output.WriteLine($"threwError: {record["threwError"]}");
        Output.WriteLine($"newTargetCorrect: {record["newTargetCorrect"]}");

        Assert.Equal(1d, record["called"]);
        Assert.Equal(1d, record["ctorCalled"]);
        Assert.Equal(true, record["threwError"]);
        Assert.Equal(true, record["newTargetCorrect"]);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_indexOf_OnlyCallsHasPropertyOnPrototypeAfterLengthZeroed()
    {
        // Test262: calls-only-has-on-prototype-after-length-zeroed.js
        // When array.length is set to 0 during indexOf iteration,
        // only [[HasProperty]] should be called on the prototype, not [[Get]]
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var getCallCount = 0;
            var hasCallCount = 0;
            var lastGetProp = "";
            var getPropLog = [];

            var array = [1, null, 3];

            Object.setPrototypeOf(array, new Proxy(Array.prototype, {
                has: function(t, pk) {
                    hasCallCount++;
                    return pk in t;
                },
                get: function(t, pk, r) {
                    getCallCount++;
                    lastGetProp = String(pk);
                    getPropLog.push(String(pk));
                    return Reflect.get(t, pk, r);
                }
            }));

            var fromIndex = {
                valueOf: function() {
                    // Zero the array's length. The loop iterates over the original
                    // length value, but the only prototype MOP method which should be
                    // called is [[HasProperty]].
                    array.length = 0;
                    return 0;
                }
            };

            Array.prototype.indexOf.call(array, 100, fromIndex);
            ({ getCallCount, hasCallCount, lastGetProp, getPropLog: getPropLog.join(",") });
        """);

        var record = Assert.IsType<JsObject>(result);
        Output.WriteLine($"getCallCount: {record["getCallCount"]}");
        Output.WriteLine($"hasCallCount: {record["hasCallCount"]}");
        Output.WriteLine($"lastGetProp: {record["lastGetProp"]}");
        Output.WriteLine($"getPropLog: {record["getPropLog"]}");

        // [[Get]] should NOT be called on the prototype for indices after length zeroed
        Assert.Equal(0d, record["getCallCount"]);
        // [[Has]] should be called 3 times (for indices 0, 1, 2)
        Assert.Equal(3d, record["hasCallCount"]);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_flatMap_CustomSpeciesConstructorWithPropertyVerification()
    {
        // Test262: this-value-ctor-object-species-custom-ctor.js
        // Tests that flatMap correctly creates properties on custom species result
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var arr = [[42, 1], [42, 2]];
            var mapperFn = function(e) { return e; };

            var called = 0;
            var ctorCalled = 0;
            var newTargetCorrect = false;
            var argCorrect = false;
            function ctor(len) {
              newTargetCorrect = new.target === ctor;
              argCorrect = len === 0;
              ctorCalled++;
            }

            arr.constructor = {
              get [Symbol.species]() {
                called++;
                return ctor;
              }
            };
            var actual = arr.flatMap(mapperFn);

            var isInstanceOfCtor = actual instanceof ctor;
            var hasOwnLength = Object.prototype.hasOwnProperty.call(actual, 'length');
            var val0 = actual[0];
            var val1 = actual[1];
            var val2 = actual[2];
            var val3 = actual[3];

            var desc0 = Object.getOwnPropertyDescriptor(actual, '0');
            var desc0Correct = desc0 && desc0.value === 42 && desc0.writable === true && desc0.enumerable === true && desc0.configurable === true;

            ({
              called,
              ctorCalled,
              newTargetCorrect,
              argCorrect,
              isInstanceOfCtor,
              hasOwnLength,
              val0,
              val1,
              val2,
              val3,
              desc0Correct,
              desc0Json: JSON.stringify(desc0)
            });
        """);

        var record = Assert.IsType<JsObject>(result);
        Output.WriteLine($"called: {record["called"]}");
        Output.WriteLine($"ctorCalled: {record["ctorCalled"]}");
        Output.WriteLine($"newTargetCorrect: {record["newTargetCorrect"]}");
        Output.WriteLine($"argCorrect: {record["argCorrect"]}");
        Output.WriteLine($"isInstanceOfCtor: {record["isInstanceOfCtor"]}");
        Output.WriteLine($"hasOwnLength: {record["hasOwnLength"]}");
        Output.WriteLine($"val0: {record["val0"]}");
        Output.WriteLine($"val1: {record["val1"]}");
        Output.WriteLine($"val2: {record["val2"]}");
        Output.WriteLine($"val3: {record["val3"]}");
        Output.WriteLine($"desc0Correct: {record["desc0Correct"]}");
        Output.WriteLine($"desc0Json: {record["desc0Json"]}");

        Assert.Equal(1d, record["called"]);
        Assert.Equal(1d, record["ctorCalled"]);
        Assert.Equal(true, record["newTargetCorrect"]);
        Assert.Equal(true, record["argCorrect"]);
        Assert.Equal(true, record["isInstanceOfCtor"]);
        Assert.Equal(false, record["hasOwnLength"]);
        Assert.Equal(42d, record["val0"]);
        Assert.Equal(1d, record["val1"]);
        Assert.Equal(42d, record["val2"]);
        Assert.Equal(2d, record["val3"]);
        Assert.Equal(true, record["desc0Correct"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_toString_DeleteObjectPrototypeToString()
    {
        // Test if delete Object.prototype.toString returns true
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("delete Object.prototype.toString");
        Output.WriteLine($"delete Object.prototype.toString = {result}");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_toString_UsesIntrinsicAfterDelete()
    {
        // Test262: non-callable-join-string-tag.js
        // After deleting Object.prototype.toString, Array.prototype.toString should
        // still work by using the intrinsic %Object.prototype.toString%
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            delete Object.prototype.toString;
            Array.prototype.toString.call({ join: null });
        """);
        Output.WriteLine($"Result = {result}");
        Assert.Equal("[object Object]", result);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_reverse_ProxyTrapsMatchSpec_WhenLengthExceedsIntegerLimit()
    {
        // Test262: length-exceeding-integer-limit-with-proxy.js
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function StopReverse() {}

            var arrayLike = {
              0: "zero",
              2: "two",
              get 4() {
                throw new StopReverse();
              },
              9007199254740987: "2**53-5",
              9007199254740990: "2**53-2",
              length: 2 ** 53 + 2,
            };

            var traps = [];

            var proxy = new Proxy(arrayLike, {
              getOwnPropertyDescriptor(t, pk) {
                traps.push(`GetOwnPropertyDescriptor:${String(pk)}`);
                return Reflect.getOwnPropertyDescriptor(t, pk);
              },
              defineProperty(t, pk, desc) {
                traps.push(`DefineProperty:${String(pk)}`);
                return Reflect.defineProperty(t, pk, desc);
              },
              has(t, pk) {
                traps.push(`Has:${String(pk)}`);
                return Reflect.has(t, pk);
              },
              get(t, pk, r) {
                traps.push(`Get:${String(pk)}`);
                return Reflect.get(t, pk, r);
              },
              set(t, pk, v, r) {
                traps.push(`Set:${String(pk)}`);
                return Reflect.set(t, pk, v, r);
              },
              deleteProperty(t, pk) {
                traps.push(`Delete:${String(pk)}`);
                return Reflect.deleteProperty(t, pk);
              },
            });

            var threw = false;
            try {
              Array.prototype.reverse.call(proxy);
            } catch (e) {
              threw = e instanceof StopReverse;
            }

            ({
              threw,
              traps,
              length: arrayLike.length,
              zero: arrayLike[0],
              oneIn: 1 in arrayLike,
              twoIn: 2 in arrayLike,
              three: arrayLike[3],
              bigIn: 9007199254740987 in arrayLike,
              bigVal: arrayLike[9007199254740988],
              bigIn2: 9007199254740989 in arrayLike,
              bigVal2: arrayLike[9007199254740990],
            });
        """);

        var record = Assert.IsType<JsObject>(result);
        Assert.Equal(true, record["threw"]);

        var trapsArray = Assert.IsType<JsArray>(record["traps"]);
        var actualTraps = trapsArray.Items
            .Select(static item => item.ToObject()?.ToString() ?? string.Empty)
            .ToArray();

        var expectedTraps = new[]
        {
            "Get:length",
            "Has:0",
            "Get:0",
            "Has:9007199254740990",
            "Get:9007199254740990",
            "Set:0",
            "GetOwnPropertyDescriptor:0",
            "DefineProperty:0",
            "Set:9007199254740990",
            "GetOwnPropertyDescriptor:9007199254740990",
            "DefineProperty:9007199254740990",
            "Has:1",
            "Has:9007199254740989",
            "Has:2",
            "Get:2",
            "Has:9007199254740988",
            "Delete:2",
            "Set:9007199254740988",
            "GetOwnPropertyDescriptor:9007199254740988",
            "DefineProperty:9007199254740988",
            "Has:3",
            "Has:9007199254740987",
            "Get:9007199254740987",
            "Set:3",
            "GetOwnPropertyDescriptor:3",
            "DefineProperty:3",
            "Delete:9007199254740987",
            "Has:4",
            "Get:4"
        };

        Assert.Equal(expectedTraps, actualTraps);

        Assert.Equal(9007199254740994d, record["length"]);
        Assert.Equal("2**53-2", record["zero"]);
        Assert.Equal(false, record["oneIn"]);
        Assert.Equal(false, record["twoIn"]);
        Assert.Equal("2**53-5", record["three"]);
        Assert.Equal(false, record["bigIn"]);
        Assert.Equal("two", record["bigVal"]);
        Assert.Equal(false, record["bigIn2"]);
        Assert.Equal("zero", record["bigVal2"]);
    }

    [Fact(Timeout = 5000)]
    public async Task Array_toString_FullTest262_NonCallableJoin()
    {
        // Full Test262 test: non-callable-join-string-tag.js
        // Step through each assertion to find which fails
        await using var engine = CreateEngine();

        // Setup Test262 harness
        await engine.Evaluate("""
            function Test262Error(message) {
              this.message = message || "";
            }
            var assert = function(mustBeTrue, message) {
              if (mustBeTrue !== true) {
                throw new Test262Error(message || 'assertion failed');
              }
            };
            assert.sameValue = function (actual, expected, message) {
              if (actual !== expected) {
                throw new Test262Error(message || ('Expected: ' + expected + ', Actual: ' + actual));
              }
            };
            assert.throws = function(ErrorCtor, fn, message) {
              try {
                fn();
              } catch (e) {
                if (e instanceof ErrorCtor) return;
                throw new Test262Error(message || ('Wrong error type: ' + e));
              }
              throw new Test262Error(message || 'Expected to throw');
            };
        """);

        // Test line 18
        Output.WriteLine("Testing: delete Object.prototype.toString");
        var deleteResult = await engine.Evaluate("delete Object.prototype.toString");
        Output.WriteLine($"delete result: {deleteResult}");
        Assert.Equal(true, deleteResult);

        // Test line 20
        Output.WriteLine("Testing: { join: null }");
        var r1 = await engine.Evaluate("Array.prototype.toString.call({ join: null })");
        Output.WriteLine($"Result: {r1}");
        Assert.Equal("[object Object]", r1);

        // Test line 45 - Arguments
        Output.WriteLine("Testing: arguments object");
        var r45 = await engine.Evaluate("Array.prototype.toString.call((function() { return arguments; })())");
        Output.WriteLine($"Result: {r45}");
        Assert.Equal("[object Arguments]", r45);

        // Test line 46 - Error
        Output.WriteLine("Testing: Error object");
        var r46 = await engine.Evaluate("Array.prototype.toString.call(new Error)");
        Output.WriteLine($"Result: {r46}");
        Assert.Equal("[object Error]", r46);

        // Test line 47 - Boolean
        Output.WriteLine("Testing: Boolean object");
        var r47 = await engine.Evaluate("Array.prototype.toString.call(new Boolean)");
        Output.WriteLine($"Result: {r47}");
        Assert.Equal("[object Boolean]", r47);

        // Test line 48 - Number
        Output.WriteLine("Testing: Number object");
        var r48 = await engine.Evaluate("Array.prototype.toString.call(new Number)");
        Output.WriteLine($"Result: {r48}");
        Assert.Equal("[object Number]", r48);

        // Test line 49 - String
        Output.WriteLine("Testing: String object");
        var r49 = await engine.Evaluate("Array.prototype.toString.call(new String)");
        Output.WriteLine($"Result: {r49}");
        Assert.Equal("[object String]", r49);

        // Test line 50 - Date
        Output.WriteLine("Testing: Date object");
        var r50 = await engine.Evaluate("Array.prototype.toString.call(new Date)");
        Output.WriteLine($"Result: {r50}");
        Assert.Equal("[object Date]", r50);

        // Test line 51 - RegExp
        Output.WriteLine("Testing: RegExp object");
        var r51 = await engine.Evaluate("Array.prototype.toString.call(new RegExp)");
        Output.WriteLine($"Result: {r51}");
        Assert.Equal("[object RegExp]", r51);

        // Test line 52 - Proxy of function
        Output.WriteLine("Testing: Proxy of function");
        var r52 = await engine.Evaluate("Array.prototype.toString.call(new Proxy(() => {}, {}))");
        Output.WriteLine($"Result: {r52}");
        Assert.Equal("[object Function]", r52);

        // Test line 53 - Proxy of Date (should be Object, not Date)
        Output.WriteLine("Testing: Proxy of Date");
        var r53 = await engine.Evaluate("Array.prototype.toString.call(new Proxy(new Date, {}))");
        Output.WriteLine($"Result: {r53}");
        Assert.Equal("[object Object]", r53);

        // Test line 54 - Custom toStringTag
        Output.WriteLine("Testing: custom Symbol.toStringTag");
        var r54 = await engine.Evaluate("Array.prototype.toString.call({ [Symbol.toStringTag]: \"Foo\" })");
        Output.WriteLine($"Result: {r54}");
        Assert.Equal("[object Foo]", r54);

        // Test line 55 - Map
        Output.WriteLine("Testing: Map");
        var r55 = await engine.Evaluate("Array.prototype.toString.call(new Map)");
        Output.WriteLine($"Result: {r55}");
        Assert.Equal("[object Map]", r55);

        Output.WriteLine("All tests passed!");
    }

}

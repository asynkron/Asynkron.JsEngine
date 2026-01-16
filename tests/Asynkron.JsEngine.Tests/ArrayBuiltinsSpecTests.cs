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

}

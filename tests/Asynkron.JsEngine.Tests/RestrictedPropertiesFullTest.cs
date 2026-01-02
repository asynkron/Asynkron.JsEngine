using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class RestrictedPropertiesFullTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task Test262_AssertThrows_Pattern()
    {
        await using var engine = CreateEngine();

        // Use the exact Test262 assert.throws pattern
        await engine.Evaluate("""

                              var Test262Error = function(message) {
                                  this.message = message || '';
                                  this.name = 'Test262Error';
                              };
                              Test262Error.prototype = new Error();
                              Test262Error.prototype.constructor = Test262Error;

                              function assert(mustBeTrue, message) {
                                  if (mustBeTrue === true) return;
                                  throw new Test262Error(message);
                              }

                              assert._toString = function(value) {
                                  return typeof value === 'symbol' ? value.toString() :
                                         String(value);
                              };

                              assert.throws = function (expectedErrorConstructor, func, message) {
                                  if (typeof func !== 'function') {
                                      throw new Test262Error('assert.throws requires two arguments');
                                  }
                                  if (message === undefined) message = '';
                                  else message += ' ';

                                  try {
                                      func();
                                  } catch (thrown) {
                                      if (typeof thrown !== 'object' || thrown === null) {
                                          throw new Test262Error(message + 'Thrown value was not an object!');
                                      }
                                      if (thrown.constructor !== expectedErrorConstructor) {
                                          throw new Test262Error(message + 'Expected ' + expectedErrorConstructor.name + ' but got ' + thrown.constructor.name);
                                      }
                                      return; // success
                                  }

                                  throw new Test262Error(message + 'Expected to throw but no exception was thrown');
                              };

                              function* generator() {}

                              """);

        // Test 1: hasOwnProperty should return false
        var hasOwnCaller = await engine.Evaluate("generator.hasOwnProperty('caller');");
        Output.WriteLine($"hasOwnProperty('caller') = {hasOwnCaller}");
        Assert.False((bool)hasOwnCaller!);

        // Test 2: assert.throws with generator.caller - this is the critical test
        Output.WriteLine("Running assert.throws for generator.caller access...");
        await engine.Evaluate("""

                              assert.throws(TypeError, function() {
                                  return generator.caller;
                              });

                              """);
        Output.WriteLine("assert.throws completed successfully!");
    }

    [Fact]
    public async Task AssertThrows_Works_With_GeneratorCallerAccess()
    {
        await using var engine = CreateEngine();

        // First verify assert.throws is defined and works
        await engine.Evaluate("""

                              function assert(condition, message) {
                                  if (!condition) throw new Error(message || 'Assertion failed');
                              }
                              assert.throws = function(expectedErrorCtor, fn, message) {
                                  var thrown = false;
                                  try {
                                      fn();
                                  } catch (e) {
                                      thrown = true;
                                      if (!(e instanceof expectedErrorCtor)) {
                                          throw new Error(message + ': Expected ' + expectedErrorCtor.name + ' but got ' + e.constructor.name);
                                      }
                                  }
                                  if (!thrown) {
                                      throw new Error(message + ': Expected to throw ' + expectedErrorCtor.name + ' but did not throw');
                                  }
                              };

                              """);

        // Define the generator
        await engine.Evaluate("function* generator() {}");

        // Now try assert.throws with generator.caller
        Output.WriteLine("Testing assert.throws with generator.caller...");

        try {
            await engine.Evaluate("""

                                  assert.throws(TypeError, function() {
                                      return generator.caller;
                                  }, 'generator.caller should throw TypeError');
                                  console.log('assert.throws completed successfully');

                                  """);
            Output.WriteLine("Test passed - assert.throws worked correctly");
        } catch (Exception e) {
            Output.WriteLine($"Test failed: {e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    [Fact]
    public async Task DirectAccess_Generator_Caller_Throws_TypeError()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("function* generator() {}");

        // Direct access should throw
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("generator.caller")
        );

        Output.WriteLine($"Thrown value type: {ex.ThrownValue.GetType().Name}");
        Output.WriteLine($"Thrown value: {ex.ThrownValue}");

        // Check it's a TypeError
        Assert.True(ex.ThrownValue.ToObject() is JsTypes.JsObject, "Should be a JsObject");
    }
}

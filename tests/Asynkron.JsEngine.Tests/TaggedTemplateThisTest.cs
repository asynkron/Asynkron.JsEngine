using Xunit;
using Xunit.Abstractions;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class TaggedTemplateThisTest
{
    private readonly ITestOutputHelper _output;

    public TaggedTemplateThisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TaggedTemplateThisBindingNonStrict()
    {
        var engine = new JsEngine();

        // First, test regular function call with undefined this - should coerce to global
        var testRegularCall = await engine.Evaluate(@"
var regularContext = null;
var regularFn = function() { regularContext = this; };
regularFn.call(undefined);
[regularContext, this, regularContext === this];
");
        var arr0 = (JsArray)testRegularCall!;
        _output.WriteLine($"Regular call - context: {arr0.Get(0).ToObject()} ({arr0.Get(0).ToObject()?.GetType().Name})");
        _output.WriteLine($"Regular call - this: {arr0.Get(1).ToObject()} ({arr0.Get(1).ToObject()?.GetType().Name})");
        _output.WriteLine($"Regular call - context === this: {arr0.Get(2)}");

        // Now test the tagged template
        var result = await engine.Evaluate(@"
var context = null;
var fn = function() {
  return function() {
    context = this;
  };
};
fn()`NoSubstitutionTemplate`;
[context, this, context === this, typeof context, typeof this];
");
        var arr = (JsArray)result!;
        var contextVal = arr.Get(0);
        var thisVal = arr.Get(1);
        _output.WriteLine($"Tagged template - context: {contextVal.ToObject()} ({contextVal.ToObject()?.GetType().Name})");
        _output.WriteLine($"Tagged template - this: {thisVal.ToObject()} ({thisVal.ToObject()?.GetType().Name})");
        _output.WriteLine($"Tagged template - context === this: {arr.Get(2)}");
        _output.WriteLine($"Tagged template - typeof context: {arr.Get(3)}");
        _output.WriteLine($"Tagged template - typeof this: {arr.Get(4)}");

        Assert.Equal(contextVal.ToObject(), thisVal.ToObject());
    }
}

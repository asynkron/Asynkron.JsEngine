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
    public void TaggedTemplateThisBindingNonStrict()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
var context = null;
var fn = function() {
  return function() {
    context = this;
  };
};
fn()`NoSubstitutionTemplate`;
[context, this, context === this, typeof context, typeof this];
");
        var arr = (JsArray)result.ToObject()!;
        _output.WriteLine($"context: {arr.Get(0)} ({arr.Get(0)?.GetType().Name})");
        _output.WriteLine($"this: {arr.Get(1)} ({arr.Get(1)?.GetType().Name})");
        _output.WriteLine($"context === this: {arr.Get(2)}");
        _output.WriteLine($"typeof context: {arr.Get(3)}");
        _output.WriteLine($"typeof this: {arr.Get(4)}");

        Assert.Equal(arr.Get(0), arr.Get(1));
    }
}

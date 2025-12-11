using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests.Test262;

[TestFixture]
public class RegressionTests
{
    [Test]
    public async Task ForInMemberLhsInvokesArrayPrototypeSetter()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var obj = Object.create(null);
            var let, value;
            obj.key = 1;

            for (let in obj) ;

            Object.defineProperty(Array.prototype, "1", {
              set: function(param) {
                value = param;
              }
            });

            for ([let][1] in obj) ;
            [
              typeof Object.getOwnPropertyDescriptor(Array.prototype, "1").set,
              value
            ];
            """);

        var resultArray = result as JsArray ?? throw new AssertionException("Expected array result");
        TestContext.WriteLine($"SetterType={resultArray.Items[0]}, Value={resultArray.Items[1]}");
        Assert.That(resultArray.Items[0], Is.EqualTo("function"));
        Assert.That(resultArray.Items[1], Is.EqualTo("key"));
    }
}

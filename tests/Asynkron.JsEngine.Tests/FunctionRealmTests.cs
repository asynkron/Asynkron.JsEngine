using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibFunction)]
public sealed class FunctionRealmTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ClassConstructorCallUsesDefiningRealmTypeError()
    {
        await using var definingEngine = CreateEngine();
        var classCtor = await definingEngine.Evaluate("(class {})");

        await using var callerEngine = CreateEngine();
        callerEngine.GlobalObject.SetProperty("Ctor", JsValue.FromObjectUnsafe(classCtor));
        callerEngine.GlobalObject.SetProperty("Expected",
            JsValue.FromObjectUnsafe(definingEngine.RealmState.TypeErrorConstructor!));

        var result = await callerEngine.Evaluate("""
            (() => {
              try {
                Ctor();
                return false;
              } catch (e) {
                return e.constructor === Expected;
              }
            })();
        """);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public async Task DerivedConstructorReturnValueUsesCurrentRealmTypeError()
    {
        await using var definingEngine = CreateEngine();
        var classCtor = await definingEngine.Evaluate("""
            (class extends Object {
              constructor() {
                return 1;
              }
            })
        """);

        await using var callerEngine = CreateEngine();
        callerEngine.GlobalObject.SetProperty("Ctor", JsValue.FromObjectUnsafe(classCtor));
        callerEngine.GlobalObject.SetProperty("Expected",
            JsValue.FromObjectUnsafe(callerEngine.RealmState.TypeErrorConstructor!));

        var result = await callerEngine.Evaluate("""
            (() => {
              try {
                new Ctor();
                return false;
              } catch (e) {
                return e.constructor === Expected;
              }
            })();
        """);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public async Task DerivedConstructorMissingSuperUsesCurrentRealmReferenceError()
    {
        await using var definingEngine = CreateEngine();
        var classCtor = await definingEngine.Evaluate("""
            (class extends Object {
              constructor() {
              }
            })
        """);

        await using var callerEngine = CreateEngine();
        callerEngine.GlobalObject.SetProperty("Ctor", JsValue.FromObjectUnsafe(classCtor));
        callerEngine.GlobalObject.SetProperty("Expected",
            JsValue.FromObjectUnsafe(callerEngine.RealmState.ReferenceErrorConstructor!));

        var result = await callerEngine.Evaluate("""
            (() => {
              try {
                new Ctor();
                return false;
              } catch (e) {
                return e.constructor === Expected;
              }
            })();
        """);

        Assert.True(Assert.IsType<bool>(result));
    }
}

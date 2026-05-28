using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibFunction)]
public sealed class FunctionRealmTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task TypeErrorFallbackUsesTypeErrorPrototypeWhenConstructorIsMissingOrReturnsUndefined()
    {
        await using var engine = CreateEngine();

        engine.RealmState.TypeErrorConstructor = null;
        var missingResult = await CaptureFallbackSignal(engine, "Object(Symbol()) + ''");

        var undefinedCtor = new HostFunction((_, _) => JsValue.Undefined, engine.RealmState);
        engine.RealmState.TypeErrorConstructor = undefinedCtor;
        var undefinedResult = await CaptureFallbackSignal(engine, "Object(Symbol()) + ''");

        AssertErrorSignal(missingResult, "TypeError");
        AssertErrorSignal(undefinedResult, "TypeError");
    }

    [Fact]
    public async Task SyntaxErrorFallbackUsesSyntaxErrorPrototypeWhenConstructorIsMissingOrReturnsUndefined()
    {
        await using var engine = CreateEngine();

        engine.RealmState.SyntaxErrorConstructor = null;
        var missingResult = await CaptureFallbackSignal(engine, "Function('}')");

        var undefinedCtor = new HostFunction((_, _) => JsValue.Undefined, engine.RealmState);
        engine.RealmState.SyntaxErrorConstructor = undefinedCtor;
        var undefinedResult = await CaptureFallbackSignal(engine, "Function('}')");

        AssertErrorSignal(missingResult, "SyntaxError");
        AssertErrorSignal(undefinedResult, "SyntaxError");
    }

    [Fact]
    public async Task ReferenceErrorFallbackUsesReferenceErrorPrototypeForUndefinedAndNullResults()
    {
        await using var engine = CreateEngine();

        var undefinedCtor = new HostFunction((_, _) => JsValue.Undefined, engine.RealmState);
        engine.RealmState.ReferenceErrorConstructor = undefinedCtor;
        var undefinedResult = await CaptureFallbackSignal(engine, "new (class extends Object { constructor() {} })()");

        var nullCtor = new HostFunction((_, _) => JsValue.Null, engine.RealmState);
        engine.RealmState.ReferenceErrorConstructor = nullCtor;
        var nullResult = await CaptureFallbackSignal(engine, "new (class extends Object { constructor() {} })()");

        AssertErrorSignal(undefinedResult, "ReferenceError");
        AssertErrorSignal(nullResult, "ReferenceError");
    }

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

    private static async Task<JsArray> CaptureFallbackSignal(JsEngine engine, string expression)
    {
        var result = await engine.Evaluate($$"""
            (() => {
              try {
                {{expression}};
                return ['no throw', false, false];
              } catch (e) {
                return [
                  e.name,
                  e instanceof globalThis[e.name],
                  Object.getPrototypeOf(e) === globalThis[e.name].prototype
                ];
              }
            })();
        """);

        return Assert.IsType<JsArray>(result);
    }

    private static void AssertErrorSignal(JsArray result, string expectedName)
    {
        Assert.Equal(expectedName, result.Items[0].AsString());
        Assert.True(result.Items[1].AsBoolean());
        Assert.True(result.Items[2].AsBoolean());
    }
}

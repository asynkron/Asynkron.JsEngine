using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// TEST BOMB: Narrow down why fresh dynamic-function class construction fails in top-level Test262 scripts.
[Category(TestCategories.StdLibFunction)]
public sealed class DynamicFunctionClassTestBomb(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string PrivateMethodClassSource = """
        let classStringExpression = `
        return class C {
          #m() { return 'test262'; }

          access(o) {
            return o.#m();
          }
        }
        `;

        let createAndInstantiateClass = function () {
          let classFactoryFunction = new Function(classStringExpression);
          let Class = classFactoryFunction();
          return new Class();
        };

        let c1 = createAndInstantiateClass();
        let c2 = createAndInstantiateClass();

        [
          c1.access(c1),
          c2.access(c2),
          (() => { try { c1.access(c2); return "no-throw"; } catch (e) { return e.name; } })(),
          (() => { try { c2.access(c1); return "no-throw"; } catch (e) { return e.name; } })()
        ];
        """;

    /// H1: The exact top-level script should work in the default engine configuration.
    [Fact(Timeout = 10000)]
    public async Task H1_TopLevelScript_DefaultEngine()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(PrivateMethodClassSource);

        AssertBrandCheckResult(result);
    }

    /// H2: The exact top-level script should also work when debug diagnostics are enabled.
    [Fact(Timeout = 10000)]
    public async Task H2_TopLevelScript_WithDebugDiagnostics()
    {
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            Logger = CurrentLogger,
            DebugMode = true,
        });

        var result = await engine.Evaluate(PrivateMethodClassSource);

        AssertBrandCheckResult(result);
    }

    /// H3: The class value returned from the dynamic factory should already be marked as a constructor.
    [Fact(Timeout = 10000)]
    public async Task H3_TopLevelScript_ReturnedClass_IsRecognizedAsConstructor()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let classStringExpression = `
            return class C {
              #m() { return 'test262'; }
              access(o) { return o.#m(); }
            }
            `;

            let classFactoryFunction = new Function(classStringExpression);
            let Class = classFactoryFunction();
            let details = [
              typeof Class,
              Class === undefined,
              Object.prototype.hasOwnProperty.call(Class, "prototype"),
              typeof Class.prototype
            ];

            try {
              let instance = new Class();
              details.push("constructed");
              details.push(instance.access(instance));
            } catch (e) {
              details.push(e.name);
              details.push(String(e && e.message ? e.message : e));
            }

            details;
            """);

        var details = Assert.IsType<JsArray>(result);
        Assert.Equal("function", details.Items[0].AsString());
        Assert.False(details.Items[1].AsBoolean());
        Assert.True(details.Items[2].AsBoolean());
        Assert.Equal("object", details.Items[3].AsString());
        Assert.Equal("constructed", details.Items[4].AsString());
        Assert.Equal("test262", details.Items[5].AsString());
    }

    /// H4: A direct top-level class expression should still be constructable outside the dynamic function path.
    [Fact(Timeout = 10000)]
    public async Task H4_TopLevelScript_DirectClassExpression_Constructs()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let Class = class C {
              #m() { return 'test262'; }
              access(o) { return o.#m(); }
            };

            let c1 = new Class();
            let c2 = new Class();
            [c1.access(c1), c2.access(c2)];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal("test262", array.Items[0].AsString());
        Assert.Equal("test262", array.Items[1].AsString());
    }

    /// H5: Top-level assignment from a dynamic-function call should preserve a primitive return value.
    [Fact(Timeout = 10000)]
    public async Task H5_TopLevelScript_DynamicFunctionCall_AssignsReturnValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return 42;");
            let value = factory();
            [value, value === 42];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(42d, array.Items[0].NumberValue);
        Assert.True(array.Items[1].AsBoolean());
    }

    /// H6: The same top-level assignment shape should work for a normal function call too.
    [Fact(Timeout = 10000)]
    public async Task H6_TopLevelScript_NormalFunctionCall_AssignsReturnValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = function () { return 42; };
            let value = factory();
            [value, value === 42];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(42d, array.Items[0].NumberValue);
        Assert.True(array.Items[1].AsBoolean());
    }

    /// H7: Returning a plain class from a dynamic function should preserve the class value.
    [Fact(Timeout = 10000)]
    public async Task H7_TopLevelScript_DynamicFunctionReturningPlainClass_AssignsClassValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return class C {};");
            let Class = factory();
            [typeof Class, Class === undefined, typeof Class.prototype];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
    }

    /// H8: Returning a class with only public methods should also preserve the class value.
    [Fact(Timeout = 10000)]
    public async Task H8_TopLevelScript_DynamicFunctionReturningPublicMethodClass_AssignsClassValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return class C { access() { return 'ok'; } };");
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H9: Returning a class with only a private field should preserve the class value.
    [Fact(Timeout = 10000)]
    public async Task H9_TopLevelScript_DynamicFunctionReturningPrivateFieldClass_AssignsClassValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return class C { #value = 'ok'; access() { return this.#value; } };");
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H10: Returning a class with a private getter should preserve the class value.
    [Fact(Timeout = 10000)]
    public async Task H10_TopLevelScript_DynamicFunctionReturningPrivateGetterClass_AssignsClassValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return class C { get #value() { return 'ok'; } access() { return this.#value; } };");
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H11: Returning a class with a private method should preserve the class value.
    [Fact(Timeout = 10000)]
    public async Task H11_TopLevelScript_DynamicFunctionReturningPrivateMethodClass_AssignsClassValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return class C { #value() { return 'ok'; } access() { return this.#value(); } };");
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H12: A one-line dynamic class with non-this private-method access should preserve the class value.
    [Fact(Timeout = 10000)]
    public async Task H12_TopLevelScript_DynamicFunctionReturningCrossObjectPrivateMethodClass_AssignsClassValue()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let factory = new Function("return class C { #value() { return 'ok'; } access(o) { return o.#value(); } };");
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access(instance)];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H13: A plain source variable should behave the same as an inline literal.
    [Fact(Timeout = 10000)]
    public async Task H13_TopLevelScript_DynamicFunctionReturningCrossObjectPrivateMethodClass_FromPlainSourceVariable()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let source = "return class C { #value() { return 'ok'; } access(o) { return o.#value(); } };";
            let factory = new Function(source);
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access(instance)];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H14: A one-line template-literal source should behave the same as a normal string source.
    [Fact(Timeout = 10000)]
    public async Task H14_TopLevelScript_DynamicFunctionReturningCrossObjectPrivateMethodClass_FromTemplateLiteralVariable()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let source = `return class C { #value() { return 'ok'; } access(o) { return o.#value(); } };`;
            let factory = new Function(source);
            let Class = factory();
            let instance = new Class();
            [typeof Class, Class === undefined, typeof Class.prototype, instance.access(instance)];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.Items[0].AsString());
        Assert.False(array.Items[1].AsBoolean());
        Assert.Equal("object", array.Items[2].AsString());
        Assert.Equal("ok", array.Items[3].AsString());
    }

    /// H15: The template-literal source text should exactly match the plain string source text.
    [Fact(Timeout = 10000)]
    public async Task H15_TopLevelScript_TemplateLiteralSourceMatchesPlainStringSource()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let templateSource = `return class C { #value() { return 'ok'; } access(o) { return o.#value(); } };`;
            let plainSource = "return class C { #value() { return 'ok'; } access(o) { return o.#value(); } };";
            [
              templateSource === plainSource,
              templateSource,
              plainSource,
              templateSource.length,
              plainSource.length
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.True(array.Items[0].AsBoolean());
        Assert.Equal(array.Items[2].AsString(), array.Items[1].AsString());
        Assert.Equal(array.Items[4].NumberValue, array.Items[3].NumberValue);
    }

    private static void AssertBrandCheckResult(object? result)
    {
        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal("test262", array.Items[0].AsString());
        Assert.Equal("test262", array.Items[1].AsString());
        Assert.Equal("TypeError", array.Items[2].AsString());
        Assert.Equal("TypeError", array.Items[3].AsString());
    }
}

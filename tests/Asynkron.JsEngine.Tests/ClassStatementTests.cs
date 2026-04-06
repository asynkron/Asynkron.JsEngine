using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for ES6 class statement features based on common Test262 class issues
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ClassStatementTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task ClassConstructorBehavior()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                constructor(x) {
                    this.x = x;
                }
            }
            var c = new C(42);
            c.x;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassMethodDefinitions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                method() {
                    return 42;
                }
            }
            var c = new C();
            c.method();
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassExtendsSuper()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(x) {
                    this.x = x;
                }
                getX() {
                    return this.x;
                }
            }
            class Derived extends Base {
                constructor(x, y) {
                    super(x);
                    this.y = y;
                }
                getY() {
                    return this.y;
                }
            }
            var d = new Derived(10, 20);
            [d.getX(), d.getY()];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(10.0, array.Items[0]);
        Assert.Equal(20.0, array.Items[1]);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassNameBindingInsideClassBody()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                getName() {
                    return C.name;
                }
            }
            var c = new C();
            c.getName();
            """);

        Assert.Equal("C", result);
    }

    [Fact(Timeout = 2000)]
    public async Task StaticMethods()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                static staticMethod() {
                    return 42;
                }
            }
            C.staticMethod();
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassTDZ_TemporalDeadZone()
    {
        await using var engine = CreateEngine();

        // This should throw a ReferenceError
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                var c = new C();
                class C { }
                """);
        });

        Assert.Contains("ReferenceError", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassNameImmutable()
    {
        await using var engine = CreateEngine();

        // The class name binding is immutable (const)
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                class C {
                    tryReassign() {
                        C = 42;
                    }
                }
                var c = new C();
                c.tryReassign();
                """);
        });

        Assert.Contains("TypeError", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassWithoutConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { }
            var c = new C();
            c instanceof C;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassPrototypeProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                method() {
                    return 42;
                }
            }
            C.prototype.method();
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassConstructorPropertyIsClass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { }
            C.prototype.constructor === C;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassMethodCreatedInsideWithCapturedClosure_UsesIrPlanInsteadOfDynamicScopeExecutor()
    {
        var logger = new TestLogger(output, "ClassMethodWithClosure", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            const scope = { value: 41 };

            function makeBox() {
                with (scope) {
                    return class Box {
                        read() {
                            return value + 1;
                        }
                    };
                }
            }

            const Box = makeBox();
            const box = new Box();
            box.read();
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing sync function via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 2000)]
    public async Task ClassConstructorCreatedInsideWithCapturedClosure_UsesIrPlanInsteadOfDynamicScopeExecutor()
    {
        var logger = new TestLogger(output, "ClassConstructorWithClosure", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            const scope = { value: 41 };

            function makeBox() {
                with (scope) {
                    return class Box {
                        constructor() {
                            this.total = value + 1;
                        }
                    };
                }
            }

            const Box = makeBox();
            new Box().total;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing sync function via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 2000)]
    public async Task ClassMethodWithDirectEval_UsesIrPlanInsteadOfDynamicScopeExecutor()
    {
        var logger = new TestLogger(output, "ClassMethodDirectEval", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            class Box {
                read(value) {
                    return eval("value + 1");
                }
            }

            new Box().read(41);
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing sync function via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 2000)]
    public async Task ClassConstructorWithDirectEval_UsesIrPlanInsteadOfDynamicScopeExecutor()
    {
        var logger = new TestLogger(output, "ClassConstructorDirectEval", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            class Box {
                constructor(value) {
                    this.total = eval("value + 1");
                }
            }

            new Box(41).total;
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing sync function via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 2000)]
    public async Task ClassSuperMethodCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                method() {
                    return 'base';
                }
            }
            class Derived extends Base {
                method() {
                    return super.method() + '-derived';
                }
            }
            var d = new Derived();
            d.method();
            """);

        Assert.Equal("base-derived", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassStaticInheritance()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                static staticMethod() {
                    return 'base';
                }
            }
            class Derived extends Base { }
            Derived.staticMethod();
            """);

        Assert.Equal("base", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassInstanceOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base { }
            class Derived extends Base { }
            var d = new Derived();
            [d instanceof Derived, d instanceof Base, d instanceof Object];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True((bool)array.GetElement(0).ToObject()!);
        Assert.True((bool)array.GetElement(1).ToObject()!);
        Assert.True((bool)array.GetElement(2).ToObject()!);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassCannotBeCalledWithoutNew()
    {
        await using var engine = CreateEngine();

        // Class constructors must be called with new
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                class C { }
                C();
                """);
        });

        Assert.Contains("TypeError", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassGetterSetter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                constructor() {
                    this._value = 0;
                }
                get value() {
                    return this._value;
                }
                set value(v) {
                    this._value = v;
                }
            }
            var c = new C();
            c.value = 42;
            c.value;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassStaticGetterSetter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C {
                static get value() {
                    return this._value || 0;
                }
                static set value(v) {
                    this._value = v;
                }
            }
            C.value = 42;
            C.value;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassNameBinding_IsConstLike()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                class C { constructor() { C = 42; } }
                new C();
                """));

        Assert.Contains("Assignment to constant variable", ex.Message);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassExtendsNull_SuperCallThrows()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                class C extends null {
                  constructor() {
                    super();
                  }
                }
                new C();
                """));

        Assert.Contains("TypeError", ex.Message);
    }
}
